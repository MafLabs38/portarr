# The catalog (`catalog.yaml`)

The catalog declares what you run: **environments**, **stacks** and **apps**. Workloads (containers) are never declared: Portarr discovers them and links them to apps through selectors (spec §4.2).

- Location: `<data folder>/catalog.yaml` (`/config/catalog.yaml` in the image), or `PORTARR__CATALOGFILE`.
- Reloaded as soon as the file is saved. A broken edit keeps the last valid catalog; the errors are shown on the Apps page and at `GET /api/v1/catalog/status`.
- Without a catalog, Portarr starts empty (D-32).

```yaml
environments:
  - id: server-1                      # lowercase letters, digits and dashes
    name: Server 1
    address: 10.0.0.10                # LAN address: LAN links are built from published ports
    docker: { url: "http://socket-proxy:2375" }   # or unix:///var/run/docker.sock

stacks:
  - id: media
    name: Media
    members:                          # the stack is Down when a critical member is Down,
      - { app: jellyfin, role: critical }   # Degraded when an optional one is not healthy
      - { app: recyclarr, role: optional }

apps:
  - id: jellyfin
    name: Jellyfin
    icon: jellyfin                    # icons/<name>.svg|png next to the catalog, else dashboard-icons; guessed from the image when omitted
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
  - id: docs
    name: Docs                        # no workload: a plain link, status Unknown
    urls: [{ href: "https://docs.homelab.example" }]
```

## Statuses

| Container | App status |
|---|---|
| Running, no healthcheck or healthcheck passing | Healthy |
| Running, healthcheck starting | Unknown |
| Running, healthcheck failing · restarting · exited | Down |
| Paused | Paused |
| Not found on its environment | Down |
| Environment unreachable | Unknown |
| No workload declared | Unknown |

An app with several workloads takes the worst of its critical ones; a failing optional workload makes it Degraded at most.

## Icons

In this order (D-47): `icons/<name>.svg|png|webp` in the folder of the catalog (your own apps, versioned with the catalog), then [dashboard-icons](https://github.com/homarr-labs/dashboard-icons) by name, downloaded once by Portarr and cached in the data folder, then a name guessed from the image (`lscr.io/linuxserver/sonarr:4` → `sonarr`), then the app's initial. Browsers only call `GET /api/v1/icons/<name>`.

## Links

When Portarr is opened on a LAN address (private IP, local name), tiles open LAN URLs; on a public name, public URLs. The Public / Local switch on the Apps page forces either; it is remembered per browser.
