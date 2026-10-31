# The catalog (`catalog.yaml`)

The catalog declares what you run: **environments**, **stacks** and **apps**. Workloads (containers, pods, Pages projects, Workers) are never declared: Portarr discovers them and links them to apps through selectors (spec §4.2).

- Location: `<data folder>/catalog.yaml` (`/config/catalog.yaml` in the image), or `PORTARR__CATALOGFILE`.
- Reloaded as soon as the file is saved. A broken edit keeps the last valid catalog; the errors are shown on the Apps page and at `GET /api/v1/catalog/status`.
- Without a catalog, Portarr starts empty (D-32).

```yaml
environments:
  - id: server-1                      # lowercase letters, digits and dashes
    name: Server 1
    address: 10.0.0.10                # LAN address: LAN links are built from published ports
    docker: { url: "http://socket-proxy:2375" }   # or unix:///var/run/docker.sock
    hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics" }   # host agent, read every second (D-72)
  - id: cdn
    name: Cloudflare
    runtime: cloudflare               # Pages projects and Workers (connectors/cloudflare.md)
    cloudflare: { accountId: "0123456789abcdef0123456789abcdef", tokenEnv: CLOUDFLARE_API_TOKEN }   # the variable name, never the token
  - id: cluster
    name: Kubernetes cluster
    runtime: kubernetes               # pods and nodes (connectors/kubernetes.md)
    kubernetes:
      server: "https://10.0.0.20:6443"
      tokenEnv: K8S_TOKEN
      caFile: cluster-ca.crt          # CA next to the catalog
      nodeHosts: { node-1: server-1 } # node-1 is a VM on server-1; other nodes (a cloud VPS) have no host
      nodeExporter:                   # node_exporter DaemonSet, read every second (D-72)
        port: 9100                    # at each node's InternalIP
        nodes: { node-2: api-server } # node-2 is not reachable from Portarr: through the API server
  - id: vm-1
    name: VM 1
    host: server-1                    # this machine is a VM on server-1 (D-59)
    docker: { url: "http://10.0.0.11:2375" }

stacks:
  - id: media
    name: Media
    members:                          # the stack is Down when a critical member is Down,
      - { app: jellyfin, role: critical }   # Degraded when an optional one is not healthy
      - { app: recyclarr, role: optional }

apps:
  - id: jellyfin
    name: Jellyfin
    icon: jellyfin                    # an icon name (not a URL): icons/<name>.svg|png next to the catalog, else dashboard-icons; guessed from the image when omitted
    stack: media                      # shorthand for a critical membership
    urls:                             # private IPs and local names are LAN URLs (scope: public|lan to force)
      - { label: Public, href: "https://jellyfin.homelab.example" }
                                      # no LAN URL declared: http://10.0.0.10:<published port> is added
    workloads:
      - { runtime: docker@server-1, match: { name: jellyfin } }   # container name
  - id: recyclarr
    name: Recyclarr
    type: service                     # web (default), desktop or service: only web apps are launchable
    workloads:
      - { runtime: docker@server-1, match: { name: recyclarr } }
  - id: trading
    name: Trading platform            # an app made of several services (D-48)
    icon: trading                     # icons/trading.png: the operator's own logo
    workloads:
      - { runtime: docker@server-1, match: { name: trading-front } }
      - { runtime: docker@server-1, match: { name: trading-api } }
      - { runtime: docker@server-1, match: { name: trading-scanner }, role: optional }   # only degrades the app
  - id: grafana
    name: Grafana
    urls: [{ href: "https://grafana.homelab.example" }]
    check:                            # overrides the default HTTP check (all keys optional)
      url: "http://10.0.0.10:3000/api/health"   # default: the public URL, else the LAN URL
      expect: [200, 401, 500-504]     # status codes or ranges; default 200-399 (redirects are not followed)
      keyword: database               # must appear in the first 64 KiB of the body
      interval: 5m                    # seconds or 30s / 5m / 1h; default 60s, 10s to 24h
      timeout: 15s                    # default 10s, at most 60s and the interval
      critical: false                 # default true: a failing check turns the app Down; false only degrades it
    workloads:
      - { runtime: docker@server-1, match: { name: grafana } }
  - id: blog
    name: Blog                        # hosted on Cloudflare Pages
    urls: [{ href: "https://blog.homelab.example" }]
    workloads:
      - { runtime: cloudflare@cdn, match: { name: blog } }   # Pages project or Worker name
  - id: shop
    name: Shop                        # runs on Kubernetes
    urls: [{ href: "https://shop.homelab.example" }]
    workloads:
      - { runtime: kubernetes@cluster, match: { namespace: shop, app: shop-web } }   # app label: survives rollouts
      - { runtime: kubernetes@cluster, match: { namespace: shop, namePrefix: shop-worker- }, role: optional }
      - { runtime: kubernetes@cluster, match: { namespace: shop, name: shop-db-0 } }  # a StatefulSet pod keeps its name
  - id: docs
    name: Docs                        # no workload: its HTTP check gives its status
    urls: [{ href: "https://docs.homelab.example" }]
  - id: wiki
    name: Wiki
    check: false                      # no HTTP check (e.g. an app that is down on purpose most of the time)
    urls: [{ href: "https://wiki.homelab.example" }]
```

## HTTP checks

Every **web** app with a URL is checked by default (D-50): `GET` on its public URL, else its LAN URL (declared or derived), every 60 s with a 10 s timeout. Up on 2xx and 3xx: redirects are not followed, so an SSO login redirect means the app answers. `check:` overrides it per app, `check: false` turns it off; a service or desktop app is only checked when it declares `check:`.

The certificate of an HTTPS URL is read on every check (D-52): less than 14 days before expiry the app is Degraded, expired or invalid it is Down. The last 90 results of each check are kept in memory (D-51); the latest is shown on the app tile, and a restart starts a new history. Results never expose raw errors, only reason codes (`timeout`, `dns-failure`, `connection-failed`, `tls-invalid`, `tls-expired`, `keyword-missing`, `http-<code>`).

## Statuses

| Container | App status |
|---|---|
| Running, no healthcheck or healthcheck passing | Healthy |
| Running, healthcheck starting | Unknown |
| Running, healthcheck failing · restarting · exited | Down |
| Paused | Paused |
| Not found on its environment | Down |
| Environment unreachable | Unknown |
| No workload declared and no check | Unknown |

| HTTP check | App status |
|---|---|
| Expected status (and keyword) | No change |
| Failing (5xx, timeout, DNS, invalid or expired certificate…) | Down; Degraded if `critical: false` |
| Certificate expiring within 14 days | Degraded |
| Not run yet (just after startup) | No change |

| Kubernetes pod | App status |
|---|---|
| Running and ready | Healthy |
| Running, a container still starting | Unknown |
| Running but not ready (readiness probe failing) · CrashLoopBackOff · image pull failing · failed | Down |
| Several pods match (replicas, rollout) | The running and ready one counts |

| Cloudflare workload | App status |
|---|---|
| Pages: latest production deployment succeeded · Worker deployed | Healthy |
| Pages: deployment failed or canceled | Down |
| Pages: deployment in progress | Unknown |

An app takes the worst of its critical members: its critical workloads and its check. A failing optional workload or non-critical check makes it Degraded at most.

## VMs and their hypervisor

Portarr cannot see a hypervisor's VMs through Docker. Tell it which machines are VMs on which host (D-59):

- `host: <environment id>` on an environment: its machine is a VM on that environment (on a Kubernetes environment: every node).
- `kubernetes.nodeHosts: { <node name>: <environment id> }`: per node; it overrides `host`. A node that is not listed (e.g. a cloud VPS) has no host.
- The host must be another environment with a single machine (`docker`, or no connector), and hosts cannot form a loop.

On the Infrastructure page, the host's card then splits its memory into **Containers**, **VMs (allocated)** (the memory each VM reports) and **Free**, and adds the VMs' load, **as the guests report it**, to its CPU (a lower bound when a guest is a Docker host, which only sees its containers). Each VM's card says "VM on <host>". Without a host agent, the hypervisor's own processes stay invisible. With one (`hostMetrics`), the host's CPU is what its kernel counts, VMs included, and its memory ring follows the host's own counters ([Host metrics](#host-metrics-node_exporter)).

## Host metrics (node_exporter)

Docker sees only its containers and metrics-server only refreshes a node every ~15 s. A **node_exporter** on a machine gives Portarr the machine as its kernel sees it, every second: overall load, I/O wait, load per core, memory, swap and CPU temperature (D-72, [connectors/node-exporter.md](connectors/node-exporter.md); installation in [companions.md](companions.md#node_exporter)).

- `hostMetrics: { nodeExporter: <URL> }` on a **Docker** environment: the `/metrics` URL of the host's node_exporter (`http` or `https`, no credentials in it).
- `kubernetes.nodeExporter:` on a Kubernetes environment, for every node:
  - `port` (default 9100): node_exporter's port at the node's InternalIP (a DaemonSet with `hostNetwork`).
  - `access`: `direct` (default, Portarr calls the node's InternalIP) or `api-server` (through the API server's **pod proxy** to the node_exporter pod running on that node, `GET /api/v1/namespaces/<namespace>/pods/<pod>:<port>/proxy/metrics`, which needs `get` on `pods/proxy` in that namespace only; the node proxy, `nodes/proxy`, would reach any port of any node and is not used).
  - `namespace` (default `monitoring`) and `selector` (default `app.kubernetes.io/name=node-exporter`): where `api-server` access finds the exporter's pod: the running pod of that namespace matching the label selector on the node (`spec.nodeName`). The pod name is remembered per node, and looked up again when the proxy answers 404 or 503 and every 5 minutes.
  - `nodes: { <node>: direct | api-server | off | <URL> }`: per node, overrides `access` (a node behind a tunnel, a node without the DaemonSet, an exporter at another address).

A machine with a host agent shows its host values first; one whose agent cannot be read keeps its connector's values (container sums, metrics-server) and says why ("node_exporter: access refused").

## Kubernetes selectors

A pod name changes on every rollout (`shop-web-7c4b9d-x2k8p`): match pods by what stays (D-56). `match:` takes `namespace`, `app` (the pod's `app` label), `namePrefix` and `name`; every key given must match, and one of `app`, `namePrefix` or `name` is required. `namespace`, `app` and `namePrefix` are only for `kubernetes@…` workloads.

## Icons

In this order (D-47): `icons/<name>.svg|png|webp` in the folder of the catalog (your own apps, versioned with the catalog), then [dashboard-icons](https://github.com/homarr-labs/dashboard-icons) by name, downloaded once by Portarr and cached in the data folder, then a name guessed from the image (`lscr.io/linuxserver/sonarr:4` → `sonarr`), then the app's initial. Browsers only call `GET /api/v1/icons/<name>`; `icon:` takes a name, never a URL.

**Your own icons**: put `<name>.svg`, `.png` or `.webp` files in an `icons/` folder **next to `catalog.yaml`**, and version them with it (same Git repository as your catalog). Mount that folder with the catalog, e.g. `./my-catalog:/catalog:ro` plus `PORTARR__CATALOGFILE=/catalog/catalog.yaml`: the icons live where your catalog lives, so rebuilding Portarr loses nothing. A new file is picked up within minutes, without restart.

**Community icons** are downloaded once, only for names your catalog uses, and cached in `<data folder>/cache/icons` (`/config` volume). Deleting the cache only means downloading them again.

## Links

When Portarr is opened on a LAN address (private IP, local name), tiles open LAN URLs; on a public name, public URLs. The Public / Local switch on the Apps page forces either; it is remembered per browser.
