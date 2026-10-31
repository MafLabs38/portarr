# V2 — Checks, infrastructure and external apps

> Status: **in progress** (MVP 2). Features: F-HLT-01 (HTTP), F-HLT-07 (TLS), F-HLT-11 (adapted), F-MET (real time), Cloudflare and Kubernetes connectors. Decisions: D-50 to D-59, D-72 to D-74 (host metrics, step 1 of the accepted plan).

## Need

> "Most of my containers have no healthcheck: 'Up' does not tell me the app answers. And some of my apps are not containers at all: they are hosted on a CDN. I want to see that they are healthy too."

> "I don't want tables of containers. I want a dashboard like Unraid's: for each machine, how it is running right now and what consumes it."

V1 shows container state. It cannot see an app that runs but returns 502 behind the reverse proxy, a certificate about to expire, a site deployed on Cloudflare Pages or Workers, or the pods of a Kubernetes cluster. Its Workloads page is a list of containers: it does not answer "is a machine struggling, and why?".

## Scope

| In | Out (later verticals) |
|---|---|
| HTTP(S) checks on app URLs, on by default for web apps; result on app tiles | TCP, DNS, ping checks (F-HLT-02); a Checks page |
| TLS certificate expiry of HTTPS URLs | Notifications on state change (V6) |
| Check results kept in memory (last 90 per check) | Persistent history, uptime over 24 h / 7 d / 30 d |
| **Infrastructure** dashboard: one card per machine (Docker host, Kubernetes node) with a live CPU chart, load bar, memory ring and top consumers; with a node_exporter: load every second, per-core bars, I/O wait, swap | Metrics history beyond 10 minutes (Prometheus, V8); disks, network |
| Kubernetes connector: pods as workloads, nodes as machines, pod usage | Pod logs; Deployments / StatefulSets as first-class objects; events |
| node_exporter read every second, streamed to the browser (D-72, D-73); companion documentation and generic manifests ([companions.md](../companions.md)) | Shipping an exporter inside Portarr |
| VMs of a hypervisor declared in the catalog (`host`, `nodeHosts`): allocation and guest load on the hypervisor's card | Reading the hypervisor itself (libvirt, Unraid API) |
| Cloudflare connector: Pages projects and Workers as workloads | Other CDNs and PaaS (Vercel, Netlify…) |

## Behavior

### HTTP checks (D-50, D-51)
- Every **web** app with a URL gets a check by default: `GET` on its public URL (else its LAN URL) every 60 s, 10 s timeout. Up when the status is 2xx or 3xx (redirects are not followed: an SSO redirect means the app answers).
- In the catalog, `check:` overrides it per app (URL, expected statuses, keyword, interval, timeout, critical) and `check: false` turns it off.
- A check is **critical** by default: a failing check turns the app Down, even when its container is Up. `critical: false` only degrades it.
- The last 90 results are kept in memory; a restart starts a new history. There is no Checks page: the result is on the app tile.

### TLS (D-52)
- The certificate of an HTTPS URL is read during the check. Less than 14 days before expiry: the app is Degraded ("certificate expires in 6 days"); expired or invalid: Down.

### Infrastructure dashboard (D-54)
- Route `/infrastructure`, sidebar entry **Infrastructure** (it replaces Workloads; `/workloads` redirects there; workload pages `/workloads/<environment>/<name>` stay).
- Environments are one light header line (name, connector, machines, or why none: not connected, unreachable, hosted). Machines are **compact cards side by side** in one responsive grid (about 400 px each): a Docker environment is one machine, a Kubernetes environment has one machine per node. A hypervisor comes first, followed by its VMs.
- A card shows, charts first: the CPU as a load bar and a line chart over the last 10 minutes (0–100 % of the machine's cores, on a time axis), the number of cores and the memory size, the memory as a ring with a legend in bytes, and the **top consumers**: the running containers or pods, heaviest CPU first, as horizontal CPU and memory bars **scaled to the heaviest consumer of the machine**, with the share of the machine written next to them (8 shown, "Show all running" for the rest). A consumer opens its workload page.
- A "Live · 2 s ago" indicator gives the age of the machine's last measurement; it turns "Stale" after three expected intervals (at least 15 s).
- Stopped containers and failing pods are not on this page (they consume nothing); statuses live on the Apps page.
- Each environment is read at its connector's pace: **Docker every 3 s** (one-shot stats of ~40 running containers take about 0.1 s on a real host), **Kubernetes every 5 s**, a point being added only when metrics-server measured again (its resolution, ~15–20 s, is shown; nothing is interpolated). The page refetches every 2 s and updates the charts in place, without animation. Points are kept **in memory only**, 10 minutes per machine. A restart starts empty charts. Long history is the job of Prometheus (V8).
- A Docker host's usage is the **sum of its containers**, labelled "Containers" (D-57): Docker does not see the host itself. A Kubernetes node's usage is the node's own (metrics.k8s.io), labelled "Node".
- An environment that is not connected or unreachable says why (reason code); its last samples stay. Cloudflare has no machine: its header line says it is hosted.

### VMs and their hypervisor (D-59)
- In the catalog, `host: <environment>` says an environment runs as a VM on another one; `kubernetes.nodeHosts: { node-1: <environment> }` says it per node (a node absent from the map, e.g. a cloud VPS, has no host). The host is a single machine (a Docker or launcher-only environment), never itself, without loops.
- The hypervisor's card counts its VMs, **as they report themselves**: the memory ring is split into **Containers**, **VMs (allocated)**, the memory each guest reports, and **Free** (total − containers − VMs), like Unraid's legend. The CPU adds the VMs' **guest-reported load** to the containers, stacked in another color on the bar and the chart; it is written as a lower bound ("≥") when a guest only sees its containers (a Docker guest) or is not sampled. The hypervisor's own processes and I/O wait are not measured: the caption says so.
- A VMs list on the hypervisor's card gives each guest's load and allocation (bars scaled to the heaviest) and links to its card, which says "VM on <host>".

### Host metrics (D-72 to D-74)
- The maintainer compared the page with Unraid's dashboard: the hypervisor's CPU read ~4 % where Unraid read 30–38 % (container sums plus guest-reported VM load miss the VMs as the host runs them, the host's processes and I/O wait), it moved every 3 s instead of every second, and swap was missing.
- A machine may declare its **node_exporter** (`hostMetrics.nodeExporter` on a Docker environment, `kubernetes.nodeExporter` for the nodes: InternalIP and port, or the API server's pod proxy to the exporter's pod, per node overrides). Portarr reads it **every second**, keeps 10 minutes in memory and pushes each read to the browser over server-sent events (`/api/v1/infrastructure/stream`), the REST snapshot being the initial state and the fallback.
- Host truth first: the card shows the overall load (100 − idle, I/O wait included) as a bar and a 1 s line chart with the I/O wait area under it, one bar per logical core, the load average and the CPU temperature, the memory ring (containers or pods, VMs allocated, system and apps, reclaimable cache, free: parts that sum to the total, "VMs and system" when allocations exceed what is used), and a swap bar ("No swap" when there is none). Top consumers and the VM list stay below; a VM with its own node_exporter reports its whole load to its hypervisor.
- A machine whose node_exporter cannot be read keeps its connector's values (container sums, metrics-server) with the reason ("node_exporter: access refused…"). Formulas: [connectors/node-exporter.md](../connectors/node-exporter.md).

### Kubernetes (D-55, D-56)
- An environment with `runtime: kubernetes`, `kubernetes: { server, tokenEnv, caFile }`. The token of a read-only ServiceAccount is read from the environment variable named in the catalog, never written in it; `caFile` is the cluster CA, relative to the catalog. Missing token or CA: not connected (`token-missing`, `ca-missing`, `ca-invalid`).
- Pods of every namespace are workloads: id `namespace/pod`, namespace and node exposed, state from the phase and container statuses (CrashLoopBackOff → restarting), readiness as health, restarts in the status text.
- Apps match pods in a way that survives rollouts: `match: { namespace, app }` (the `app` label), `namePrefix`, or `name`. When several pods match, the running and ready one is shown.
- Pod usage comes from metrics.k8s.io; pod logs are not served yet.

### Docker host metrics (D-57)
- Capacity from `GET /info` (NCPU, MemTotal). A socket proxy without `INFO=1` refuses it: the capacity is then taken from the containers' stats.
- Per-container stats: one-shot samples (`stream=false&one-shot=true`) of the running containers, 4 at a time per host; CPU is measured between two samples.
- `IHostMetricsSource` is the seam for a host agent: node_exporter over HTTP, or through the Kubernetes API server (D-72). Nothing is shipped inside Portarr; the companion is installed next to it ([companions.md](../companions.md)).

### Cloudflare (D-53)
- An environment with `runtime: cloudflare` and an account id lists the account's **Pages projects** and **Workers** as workloads. The API token is read from an environment variable named in the catalog.
- A Pages project: state from its latest production deployment (success → running, failure → exited, in progress → starting), with the deployment date and commit. A Worker: running when deployed.

### UI on app tiles
- A third line under the container and the healthcheck: the check ("200 · 140 ms", "HTTP 502", "timeout", "certificate expires in 6 days"). Apps without a container show their check as the main line. A web app with no check shows "not monitored".

## Statuses

An app's status is the worst of its critical members: workloads (V1, Kubernetes pods) and its check. Optional workloads and non-critical checks only degrade it.

## Acceptance criteria

- [ ] Every web app of the real catalog has a check result within 90 s, without editing the catalog
- [ ] An app whose container is Up but whose URL returns 5xx or times out is Down, with the reason on the tile
- [ ] A certificate expiring within 14 days degrades the app and says when
- [ ] The Cloudflare sites show their deployment state and their check; a failed deployment turns the app Down
- [ ] `check: false` removes the check; a custom `check:` (other URL, keyword) is honored
- [ ] The Infrastructure page shows one compact card per Docker host and per Kubernetes node, side by side, with a CPU chart that moves within 5 s on a Docker host, the age of the last sample, the memory ring, and the top running consumers (bars scaled to the heaviest) linking to their pages
- [ ] A hypervisor whose VMs are declared with `host` / `nodeHosts` shows Containers, VMs (allocated) and Free memory, and the VMs' guest-reported load; each VM says "VM on <host>"
- [ ] A Docker host's usage is labelled as the sum of its containers
- [ ] A machine with a node_exporter shows its load every second (per-core bars, I/O wait, swap), within a few points of `top` / `free` on the host; the hypervisor's load matches Unraid's dashboard
- [ ] A node read through the API server without `pods/proxy` says "access refused" and keeps metrics-server's values
- [ ] An app matched by `namespace` + `app` keeps its pod after a rollout (new pod name)
- [ ] A Kubernetes environment without its token shows "not connected · token-missing"; with a read-only token, its nodes and pods appear
- [ ] No secret in the catalog: Cloudflare and Kubernetes tokens come from environment variables
