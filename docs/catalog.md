# The catalog (`catalog.yaml`)

The catalog declares what you run: **environments**, **stacks** and **apps**. Workloads (containers) are never declared: Portarr discovers them and links them to apps through selectors (spec §4.2).

- Location: `<data folder>/catalog.yaml` (`/config/catalog.yaml` in the image), or `PORTARR__CATALOGFILE`.
- Reloaded as soon as the file is saved. A broken edit keeps the last valid catalog; the errors are shown on the Apps page and at `GET /api/v1/catalog/status`.
- Without a catalog, Portarr starts empty (D-32).

```yaml
environments:
  - id: server-1                      # lowercase letters, digits and dashes
    name: Server 1
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
    icon: jellyfin                    # a dashboard-icons name, or a full image URL
    stack: media                      # shorthand for a critical membership
    urls:                             # the first URL opens when the tile is clicked
      - { label: Public, href: "https://jellyfin.homelab.example" }
      - { label: LAN, href: "http://10.0.0.10:8096" }
    workloads:
      - { runtime: docker@server-1, match: { name: jellyfin } }   # container name
  - id: recyclarr
    name: Recyclarr
    type: service                     # web (default), desktop or service: only web apps are launchable
    workloads:
      - { runtime: docker@server-1, match: { name: recyclarr } }
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

An app with several workloads takes the worst of them.
