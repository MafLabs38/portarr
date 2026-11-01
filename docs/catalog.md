# The catalog (`catalog.yaml`)

The catalog declares what you run: **environments**, **stacks** and **apps**, plus the **data you follow** (and which backup tools should protect it), the **code sources** you make apps from, and **what you know** about the fleet for people and AI assistants: notes, how to reach each machine, network links and external providers ([Knowledge](#knowledge-for-people-and-assistants)). Workloads (containers, pods, Pages projects, Workers) are never declared: Portarr discovers them and links them to apps through selectors (spec §4.2).

- Location: `<data folder>/catalog.yaml` (`/config/catalog.yaml` in the image), or `PORTARR__CATALOGFILE`.
- Reloaded as soon as the file is saved. A broken edit keeps the last valid catalog; the errors are shown on the Apps page and at `GET /api/v1/catalog/status`.
- Without a catalog, Portarr starts empty (D-32).
- It can also be edited through the API and the MCP write tools, off by default ([Editing through the API](#editing-through-the-api)); the file stays the source of truth.

```yaml
environments:
  - id: server-1                      # lowercase letters, digits and dashes
    name: Server 1
    address: 10.0.0.10                # LAN address: LAN links are built from published ports
    docker: { url: "http://socket-proxy:2375" }   # or unix:///var/run/docker.sock
    hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics" }   # host agent, read every second (D-72)
    # or, in place of the two lines above, one line for the Portarr reader of the machine (D-100, D-101):
    # reader: { url: "http://10.0.0.10:8485", tokenEnv: PORTARR_READER_SERVER_1 }
    backrest: { url: "http://10.0.0.10:9898", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD }   # backup tool of this machine (D-80)
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
      # allowInsecure: true          # accepts an http:// server, for a lab or a test only (D-103); https is required otherwise
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
      type: http                      # the kind of check; http is the only one for now
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

## Identity of the portal

```yaml
portal:
  name: Homelab                 # shown at the bottom of the menu; default "Portarr"
  logo: branding/logo.png       # relative to the catalog folder; default: Portarr's own logo
  colors:                       # colors of the two texts; default: Portarr's accent (the yellow of its logo)
    name: "#ffc145"             # a hex color, quoted (an unquoted # starts a YAML comment)...
    version: inkMuted           # ...or a theme token name
```

Every key is optional (D-91). The sidebar shows, straight on its background (no card) and always pinned at the bottom of the visible window, the name, the logo and the version of the running Portarr. The image alone is clipped to rounded corners, without a border: a transparent PNG looks unchanged, a square image has no harsh corners.

- `name`: one line, at most 40 characters.
- `logo`: a path **inside the folder of the catalog**, with forward slashes: no `..`, no absolute or drive path, no hidden file or folder, no link leading out of the folder. PNG, WebP or SVG, at most 1 MiB, and really that format (an SVG with a script, an event handler or a `foreignObject` is refused). A logo that cannot be used falls back to Portarr's own, and System, Status says why. An SVG is served with the same strict policy as icons (`default-src 'none'; sandbox`).
- `colors.name`, `colors.version`: `#rgb`, `#rrggbb`, or a theme token name: `accent` (default), `accentDeep`, `secondary`, `ink`, `inkSoft`, `inkMuted`. Nothing else is accepted (no `rgb()`, no `url()`, no other CSS); an invalid value is a catalog error.
- Change it by hand, in System, Status (Identity card: name, colors, upload; needs [catalog writes](#editing-through-the-api) and the API key), through `PUT /api/v1/portal` (`name` and/or `colors: { name, version }`; an empty value goes back to the default) and `PUT /api/v1/portal/logo` (multipart field `file`, stored in `branding/` next to the catalog, the previous file of that name kept as `.bak-<timestamp>`), or through the MCP tools `get_portal` and `set_portal` (`name`, `logo` chosen among the files already in `branding/`, `nameColor`, `versionColor`). `GET /api/v1/portal` returns `colors` resolved (accent where the catalog says nothing).

```bash
curl -s -X PUT localhost:8484/api/v1/portal -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' -d '{"name":"Homelab"}'
curl -s -X PUT localhost:8484/api/v1/portal -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' -d '{"colors":{"name":"#ffc145","version":"inkMuted"}}'
curl -s -X PUT localhost:8484/api/v1/portal/logo -H "X-Api-Key: $KEY" -F file=@logo.png
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

- `hostMetrics: { nodeExporter: <URL> }` on a **Docker** environment: the `/metrics` URL of the host's node_exporter (`http` or `https`, no credentials in it). `interval` (optional, `5s`, `1m`, from 1 second to 5 minutes, default every second) reads it less often, for a machine behind a slow link or a large fleet (D-107).
- `reader: { url, tokenEnv }` on a **Docker** environment: the [Portarr reader](connectors/reader.md) of the machine, in place of `docker` and `hostMetrics` (declaring both is an error). `url` is the reader's address (it may carry a path behind a reverse proxy), `tokenEnv` the name of the variable that holds its token, which **must start with `PORTARR_READER_`**. `docker` and `hostMetrics` also accept an optional `tokenEnv` for a proxy that asks for a bearer token, in the same namespace.
- `kubernetes.nodeExporter:` on a Kubernetes environment, for every node:
  - `port` (default 9100): node_exporter's port at the node's InternalIP (a DaemonSet with `hostNetwork`).
  - `access`: `direct` (default, Portarr calls the node's InternalIP; a node whose address is not an IP, or is a loopback, link-local or unspecified one, is shown with the reason `invalid-address`; give it a route in `nodes`) or `api-server` (through the API server's **pod proxy** to the node_exporter pod running on that node, `GET /api/v1/namespaces/<namespace>/pods/<pod>:<port>/proxy/metrics`, which needs `get` on `pods/proxy` in that namespace only; the node proxy, `nodes/proxy`, would reach any port of any node and is not used).
  - `namespace` (default `monitoring`) and `selector` (default `app.kubernetes.io/name=node-exporter`): where `api-server` access finds the exporter's pod: the running pod of that namespace matching the label selector on the node (`spec.nodeName`). The pod name is remembered per node, and looked up again when the proxy answers 404 or 503 and every 5 minutes.
  - `nodes: { <node>: direct | api-server | off | <URL> }`: per node, overrides `access` (a node behind a tunnel, a node without the DaemonSet, an exporter at another address).
  - `interval` (optional, `5s`, `1m`, from 1 second to 5 minutes, default every second): how often each node is read. With `api-server` access every read goes through the API server, so a larger cluster or a slower link is a reason to raise it (D-107).

A machine with a host agent shows its host values first; one whose agent cannot be read keeps its connector's values (container sums, metrics-server) and says why ("node_exporter: access refused").

## Kubernetes selectors

A pod name changes on every rollout (`shop-web-7c4b9d-x2k8p`): match pods by what stays (D-56). `match:` takes `namespace`, `labels` (any labels of the pod, e.g. `{ app.kubernetes.io/name: shop-api }`, which is what Helm charts set), `app` (the shorthand of the `app` label), `namePrefix` and `name`; every key given must match, and one of `app`, `labels`, `namePrefix` or `name` is required. `namespace`, `labels`, `app` and `namePrefix` are only for `kubernetes@…` workloads (D-56, D-106).

## Data

The data you follow, so the **Data** page can tell whether you could get your state back (D-77). It is declarative, like apps: **only what is listed here appears**, nothing is discovered. Portarr runs no backup; each target names the job of an existing tool that should produce the copy, and Portarr compares that with what the tool reports ([verticals/V3-data-backups.md](verticals/V3-data-backups.md), companions: [Backrest](companions.md#backrest), [K8up](companions.md#k8up)).

```yaml
environments:
  - id: server-1
    docker: { url: "http://10.0.0.10:2375" }
    backrest:                              # the Backrest of this machine (optional, one per Docker or launcher-only environment)
      url: "http://10.0.0.10:9898"         # no credentials in the URL
      userEnv: BACKREST_USER               # names of environment variables, never the credentials; both or neither
      passwordEnv: BACKREST_PASSWORD

data:
  - id: shop-db                            # required, unique
    app: shop                              # required: an app id
    name: Shop database                    # default: the id
    kind: postgresql                       # required, free: postgresql, mariadb, influxdb, vault, sqlite-app-config, volume, files, cluster-state…
    where:                                 # required environment; the rest is informative (not checked against the inventory)
      environment: cluster
      namespace: shop
      workload: shop-db-0                  # container, pod or StatefulSet
      claim: data-shop-db-0                # PersistentVolumeClaim
      path: /var/lib/postgresql/data       # or a host path
    importance: critical                   # required: critical | important | comfort
    maxAge: 12h                            # optional: overrides the importance's loss window (30m, 24h, 7d)
    targets:                               # required: the copies expected
      local:                               # on the premises (the array)
        cronjob: shop/db-dump              # a Kubernetes CronJob: <namespace>/<name>
      offsite:                             # in a bucket (B2, S3)
        k8up: { schedule: shop/offsite, item: data-shop-db-0 }   # a K8up Schedule, optionally narrowed to one claim
        # backrest: { plan: shop-db-offsite }                    # a Backrest plan (environment: of the data, or environment: <id>)
        # manual: "2026-01-31T03:00:00Z"                         # your own copy: true, or the date of the last one
```

- **Importance** is the accepted loss window: a copy whose newest success is older than the window (plus one hour of grace) is late. **critical** 24 h, **important** 7 days, **comfort** 30 days; `maxAge` overrides it.
- **Targets**: `local` and `offsite`; at least one. A target you leave out is a copy you do not want (the page shows it "not followed"). A target with no source (`offsite: {}` or `offsite:`) is a copy you want and nothing produces yet: the page flags it **Missing**.
- **Sources** of a target (any mix; one proving a recent copy is enough):
  - `backrest: { plan: <id>, environment?: <id> }`: a plan of the Backrest declared on that environment (the data's own by default; it must be a machine, not a cluster). A plan writes to one repository, so one copy is one plan.
  - `k8up: { schedule: <namespace>/<name>, item?: <text>, environment?: <id> }`: a K8up Schedule of a Kubernetes environment. `item` narrows the Schedule to the snapshots whose path contains it (a claim name), so a Schedule that backs up a whole namespace proves *this* claim.
  - `cronjob: <namespace>/<name>`: a Kubernetes CronJob (needs `get, list` on `cronjobs` and `jobs`, see [connectors/kubernetes.md](connectors/kubernetes.md)).
  - `manual: true | "<ISO 8601 date>"`: a copy you make yourself; Portarr cannot read it. With a date it is judged against the window, without one it stays Unknown.
- A source whose tool is not configured yet (no `backrest:` on the environment) is allowed: the copy is Not set up (`no-connector`) until the tool is deployed, which lets you declare the policy before the tools exist.
- Validation is strict: unknown app or environment, missing importance, kind or target, a K8up or CronJob source on a machine, a Backrest source on a cluster, a malformed duration or date, and `backrest:` credentials that are not variable names are catalog errors naming the entry. The former `backups:` section is gone: migrate each job to the `targets` of the data it covers.

States, per copy and per asset: **Protected**, **Waiting for first run** (the job exists and has not run yet), **Unknown** (the tool cannot be read), **Not set up** (the tool is not configured on the environment), **Late**, **Failed**, **Missing** (D-79).

## Code sources

The GitHub accounts whose repositories the **Maintenance** page scans (D-64): every app you make, deployed here or not (a desktop app, a library, a tool). Portarr only reads them ([connectors/github.md](connectors/github.md)).

```yaml
sources:
  github:
    owners: [example, example-org]   # required (or include): user or organization logins
    tokenEnv: GITHUB_TOKEN           # default GITHUB_TOKEN: the variable name, never the token
    pushedWithinMonths: 6            # default 6: only repositories pushed recently; 0 scans them all
    include: [example/legacy-tool]   # always scanned, even inactive or owned by someone else
    exclude: ["example/test-*", "*/sandbox"]   # never scanned; * matches anything
    forks: false                     # default false
    archived: false                  # default false

apps:
  - id: shop
    repository: example/shop         # the app's code (owner/name): links the app and its repository (D-65)
```

- A repository is shown as **running in the fleet** when an app declares it in `repository:`, when a running image carries its URL in the OCI label `org.opencontainers.image.source`, or when a running image is named like it (`ghcr.io/example/shop`, Docker Hub `example/shop`). The others are listed as **not deployed here**.
- Running images need no declaration: every running container and pod is checked against its registry ([connectors/registries.md](connectors/registries.md)).

## Knowledge for people and assistants

What you would tell a new colleague, or an AI assistant, about your fleet (D-84): what a machine is for, how to reach it, how machines are wired together, which external services you depend on. Portarr only describes it: it never connects with it. It feeds the Assistant's knowledge base (`portarr://knowledge/infrastructure`) and the MCP tool `get_environment_knowledge`.

```yaml
environments:
  - id: server-1
    name: Server 1
    notes: |                             # free Markdown, at most 8000 characters
      The hypervisor. Stop the array before a reboot.
    access:                              # how to reach this machine (never a secret)
      ssh: { alias: server-1, host: 10.0.0.10, port: 22, user: root, key: ~/.ssh/server-1, jump: bastion }
      consoles: [{ label: Web UI, href: "https://server-1.homelab.example" }]
      notes: From outside, through the VPN first.
  - id: cluster
    name: Cluster
    runtime: kubernetes
    kubernetes: { server: "https://10.0.0.20:6443", tokenEnv: K8S_TOKEN, nodeHosts: { node-1: server-1 } }
    machines:                            # what you know about the machines of an environment (cluster nodes, VPS)
      - name: node-2                     # the host or node name
        notes: A VPS at a hosting provider; no Docker on it.
        access: { ssh: { alias: node-2, user: ubuntu, key: id_ed25519 } }

stacks:
  - { id: web, name: Web, members: [{ app: blog }], notes: Public sites. }
apps:
  - { id: blog, name: Blog, notes: "Built by CI, deployed on push." }

links:                                   # network links between environments or machines
  - id: tunnel
    from: node-1                         # an environment id, a machine name, or environment/machine
    to: node-2
    kind: wireguard-tunnel               # wireguard-tunnel | vpn | lan | peering | ssh-tunnel | other
    name: wg0
    network: 10.99.0.0/24                # optional, CIDR
    notes: node-1 initiates (it is behind a NAT); node-2 listens on 51820/udp.

providers:                               # external providers and accounts you use
  - id: cdn
    name: Example CDN
    kind: cdn                            # cdn | paas | dns | registrar | cloud | vps | storage | backup | email | git | registry | monitoring | other
    console: "https://dash.cdn.example"
    account: ops@homelab.example         # which account: a name or an e-mail hint, never a credential
    usedBy: [blog, cluster]              # app and environment ids
    notes: Pages for the blog, DNS of homelab.example.
```

- `notes` exist on environments, machines, stacks, apps, links and providers.
- `access.ssh` takes an `alias` (from your SSH config) or a `host`, and optionally `port`, `user`, `key` (the **name or path** of the key file) and `jump` (ProxyJump). Assistants are told `ssh <alias>`, or the full command line.
- A machine name can be one of `machines`, or a node of `kubernetes.nodeHosts`; a link end that is a machine of several environments must be written `environment/machine`.
- **No secrets**: a value that looks like one (a private key or a certificate, SSH key material, a well-known token format, `password=…`, credentials in a URL, a long random string) is a catalog error that names the field, never the value. Name the key file or the environment variable instead.

## Editing through the API

The catalog file stays the source of truth (D-44); Portarr can edit it for you, or for an assistant, instead of you editing YAML (D-83 to D-86). Off by default:

| Setting | Effect |
|---|---|
| `PORTARR__CATALOG__WRITABLE=true` | Turns the write endpoints on. Who may write is the `catalog.write` permission of a role (V7): a signed-in user, or a per-user API key of such a user. |
| `PORTARR__APIKEY` | Deprecated bootstrap key (at least 32 characters, e.g. `openssl rand -hex 32`), sent in `X-Api-Key` or `Authorization: Bearer`: acts as the built-in `system` principal with Admin permissions. Optional; a warning is logged at startup when set. |
| `PORTARR__CATALOG__BACKUPS` | Previous versions kept next to the catalog (`catalog.yaml.bak-<timestamp>`), default 10. |
| `PORTARR__MCP__WRITETOOLS=true` | Adds the MCP write tools ([verticals/V5-ai-mcp.md](verticals/V5-ai-mcp.md)); they also need the catalog to be writable. |

```bash
curl -s localhost:8484/api/v1/catalog/status                               # → "version": "3f2a…", "writable": true
curl -s localhost:8484/api/v1/catalog/apps/blog                            # the item as the file holds it, with the version
curl -s -X PUT localhost:8484/api/v1/catalog/apps/blog/notes -H "X-Api-Key: $KEY" \
  -H 'Content-Type: application/json' -d '{"version":"3f2a…","notes":"Built by CI."}'
```

- Endpoints: `GET|PUT|DELETE /api/v1/catalog/{section}/{id}`, `POST /api/v1/catalog/{section}`, `PUT /api/v1/catalog/{section}/{id}/notes`, `PUT|DELETE /api/v1/catalog/environments/{id}/machines/{name}`, `GET /api/v1/catalog/audit`; the identity has its own ([Identity of the portal](#identity-of-the-portal)); sections `environments`, `stacks`, `apps`, `data`, `links`, `providers`. Items are JSON with the keys of the file. Reference at `/docs`.
- **Versions**: every write sends the version it read; if the file changed since (by hand, by another client), the write is refused with 409 and the current version. Writes are serialized.
- **Validation**: the whole resulting file goes through the parser; an error is a 400 whose `errors` are keyed by field path (`apps[blog].check.url`) and the file is not touched.
- **What changes in the file**: only what differs. A changed value is rewritten in place, a new key or item is added after the last one with the file's indentation and style (an item of a list written `- { … }` is written that way), a removed one loses its lines; comments and formatting elsewhere stay. Limits: a comment *inside* a rewritten node is lost (a flow item is rewritten whole; a list whose order changed is rewritten), and a comment line above a removed item stays.
- **Safety**: the file is written to a temporary file and renamed over the catalog (atomic); a catalog bind-mounted as a single file is rewritten in place instead. Each write keeps the previous version and is appended to `catalog.audit.jsonl` next to the catalog (when, channel `api` or `mcp`, client address, operation, item, versions before and after, which keys changed; never values or keys). 30 writes per minute per client.

## Icons

In this order (D-47): `icons/<name>.svg|png|webp` in the folder of the catalog (your own apps, versioned with the catalog), then [dashboard-icons](https://github.com/homarr-labs/dashboard-icons) by name, downloaded once by Portarr and cached in the data folder, then a name guessed from the image (`lscr.io/linuxserver/sonarr:4` → `sonarr`), then the app's initial. Browsers only call `GET /api/v1/icons/<name>`; `icon:` takes a name, never a URL.

**Your own icons**: put `<name>.svg`, `.png` or `.webp` files in an `icons/` folder **next to `catalog.yaml`**, and version them with it (same Git repository as your catalog). Mount that folder with the catalog, e.g. `./my-catalog:/catalog:ro` plus `PORTARR__CATALOGFILE=/catalog/catalog.yaml`: the icons live where your catalog lives, so rebuilding Portarr loses nothing. A new file is picked up within minutes, without restart.

**Community icons** are downloaded once, only for names your catalog uses, and cached in `<data folder>/cache/icons` (`/config` volume). Deleting the cache only means downloading them again.

## Links

When Portarr is opened on a LAN address (private IP, local name), tiles open LAN URLs; on a public name, public URLs. The Public / Local switch on the Apps page forces either; it is remembered per browser.
