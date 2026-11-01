# V1 — Launcher and Docker

> Status: **in progress** (MVP 1). Features: F-CAT-01/02/04/06/07, F-HLT-03, F-INV-01/02, F-LOG-01, F-MET (real time). Decisions: D-32, D-33, D-44 to D-48.

## Need

> "I want one page where I open any of my apps in one click, from home or from the Internet, and where I see at a glance which ones are down, without opening Portainer on each host."

Today the operator juggles a launcher, Portainer on several hosts and the reverse proxy configuration. Nothing says that *the media stack is broken because Sonarr is down*.

## Scope

| In | Out (later verticals) |
|---|---|
| Catalog file: environments, stacks, apps, icons, URLs | Editing the catalog from the UI (D-44) |
| Docker hosts through a read-only socket proxy | Kubernetes (V4), hypervisor (V7) |
| Container state, healthcheck, live CPU and memory, live logs | Metrics history (V8), log search (V9) |
| Status of apps and stacks | Notifications (V6), issues (V5) |
| Public and LAN URLs | Routes read from the reverse proxy (V3) |

## Behavior

### Catalog
- One YAML file ([format](../catalog.md)), reloaded on save; a broken edit keeps the last valid catalog and shows the errors on the Apps page.
- An environment declares its Docker endpoint and its LAN `address` (used to build LAN URLs).
- An app lists its workloads (container name per environment), each `critical` or `optional` (D-48).

### Apps page (launcher)
- Apps grouped by stack, in catalog order; apps without a stack last. Each stack shows its status and, when not healthy, why ("Sonarr is down").
- A tile shows: the icon, the name, **the container state** ("Up 3 h", "Exited (1) 2 min ago") and **the healthcheck** ("healthy", "unhealthy", "starting", "no healthcheck") (D-45). With several workloads: "7/8 up", the worst one detailed in the tooltip. The frame color is the app status.
- A click opens the app's URL for the current network (D-46): LAN URL when Portarr is opened on a LAN address, public URL otherwise; a Public/Local switch forces either. Other URLs are small links under the tile. Services and desktop apps are not launchable.

### Icons (D-47)
1. `icons/<name>.svg|png|webp` next to the catalog (the operator's custom apps);
2. [dashboard-icons](https://github.com/homarr-labs/dashboard-icons) by name, fetched once by Portarr and cached;
3. a name guessed from the image (`lscr.io/linuxserver/sonarr:4` → `sonarr`);
4. the app's initial.

The browser only ever calls `GET /api/v1/icons/{name}`.

### Workloads
- Every container per environment, with status, image, Compose project and the app it serves; unreachable environments flagged, last known containers kept and dimmed.
- Container page: overview, live CPU (vCPU) and memory every 5 s, live logs (last 200 lines then new ones, stderr highlighted, pause).

## Statuses

See [catalog.md § Statuses](../catalog.md#statuses). Stack: Down when a critical member is Down, Degraded when any member is not Healthy.

## Acceptance criteria

- [ ] With a catalog of real hosts, every app shows its container state and healthcheck within 15 s
- [ ] A container stopped on purpose turns its app Down and its stack Down (critical) or Degraded (optional), naming it
- [ ] Opened from a LAN address, tiles open LAN URLs; opened from the public DNS, public URLs; the switch overrides
- [ ] An app without a declared URL but with a published port gets a LAN link
- [ ] A custom icon dropped in `icons/` is used without restart; common apps get their icon without `icon:`
- [ ] No request leaves the browser for a third-party host
- [ ] Live logs of any container start in under 2 s
- [ ] A typo in the catalog shows a clear error and keeps the previous apps
