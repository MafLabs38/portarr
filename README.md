# Portarr

> Your homelab, at a glance. A self-hosted portal that launches your apps **and** tells you what is wrong — across Docker hosts, VMs, hypervisors and Kubernetes clusters.

Portarr is built in the spirit of the *arr suite (Sonarr, Radarr, Prowlarr…): a single container, an API-first design, pluggable **providers**, and a familiar interface. It does not reinvent your tools — it **orchestrates** them: Docker, Kubernetes, Prometheus, Loki, Grafana, Trivy, your reverse proxy and your identity provider.

> **Status: first MVP.** Launcher and Docker monitoring work; follow the [implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Why

A typical homelab grows to dozens of services on several machines. Knowing whether everything is fine means juggling a launcher, a container UI, an uptime checker, Grafana, an image-update notifier and a vulnerability scanner — none of which knows that *Sonarr belongs to the Media stack, runs on server-1, is deployed from a given IaC repo, has two critical CVEs and an update waiting*.

Portarr connects those dots.

## Features (planned)

- **Launcher** — every app with its real icon, grouped by stack, `Ctrl+K` search, desktop apps included.
- **Health** — HTTP/TCP/ping checks, native container health, heartbeats for jobs, stack viability rules.
- **Infrastructure** — hypervisors → VMs → containers / pods, in one tree; inside *and* outside views of each VM.
- **Resources** — "who consumes what" explorer, capacity and over-allocation, disk-full forecasts.
- **Logs** — live tail for containers and pods, history via Loki, probable cause of unexpected stops.
- **Data** — databases, storage and **backup verification** (is the last backup recent, external, plausible?).
- **Maintenance** — image updates, deployed vs declared vs released versions, end-of-life runtimes, Dependabot alerts.
- **Security** — image CVEs (Trivy), dependency vulnerabilities, login events.
- **Alerts** — e-mail, in-app and browser push notifications, periodic digest.
- **As code** — catalog in YAML or container labels, Terraform provider, Helm chart.

See the full [specification](docs/SPEC.md).

## Documentation

| Document | Content |
|---|---|
| [SPEC.md](docs/SPEC.md) | Vision, decisions, domain model, providers, screens, features, roadmap |
| [DESIGN.md](docs/DESIGN.md) | UI guidelines: *arr patterns and Portarr theme |
| [TESTING.md](docs/TESTING.md) | Test strategy: unit, integration, end-to-end |
| [IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Core first, then vertical slices — one PR each |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute |
| [SECURITY.md](SECURITY.md) | Security policy |

## Tech stack

.NET 10 · ASP.NET Core · SignalR · SQLite / PostgreSQL · React · TypeScript · Vite · TanStack Query · Playwright · Testcontainers · GitHub Actions.

## Development

Requirements: .NET 10 SDK, Node.js 22 with Corepack (Yarn 1), Docker for integration and end-to-end tests.

```bash
dotnet run --project src/Portarr.Host        # API on http://localhost:8484
yarn install && yarn start                   # UI on http://localhost:5173 (proxies /api)
```

All commands (tests, lint, e2e) are listed in [CLAUDE.md](CLAUDE.md#commands).

## Configuration

Settings come from `appsettings.json`, overridden by `PORTARR__*` environment variables (`__` separates sections).

| Variable | Default | Purpose |
|---|---|---|
| `PORTARR__URLS` | `http://+:8484` | Listening address |
| `PORTARR__DATAFOLDER` | `/config` in the image, the user's local data folder otherwise | SQLite database and future backups |
| `PORTARR__DATABASE__TYPE` | `Sqlite` | `Sqlite` or `Postgres` |
| `PORTARR__DATABASE__CONNECTIONSTRING` | — | Required for PostgreSQL, e.g. `Host=db;Database=portarr;Username=portarr;Password=…` |
| `PORTARR__CATALOGFILE` | `<data folder>/catalog.yaml` | The [catalog](docs/catalog.md) of environments, stacks and apps |

Connectors: [Docker](docs/connectors/docker.md).

Probes: `GET /health/live` (the process answers) and `GET /health/ready` (the database answers).

## License

[GPL-3.0](LICENSE), like the *arr apps.
