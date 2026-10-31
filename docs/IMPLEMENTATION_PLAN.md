# Portarr — Implementation Plan

> **Status**: v1, 2026-10-28.
> **Principle**: first a solid, tested **core**, then **vertical slices**. A vertical slice delivers a provider (or a cross-cutting capability) and the features that depend on it, from the database all the way to the screen.
> **One vertical slice = one pull request** (or a small stack of linked PRs), mergeable on its own, with its three levels of tests green ([TESTING.md](TESTING.md)) and a UI compliant with [DESIGN.md](DESIGN.md).
> The `F-…` and `D-…` identifiers refer to the [spec](SPEC.md).

---

## 1. Ground rules

### 1.1 Definition of Done (DoD) for a vertical slice

A vertical slice PR is merged only if **every** box is checked:

- [ ] **Unit**: the slice's business rules are covered, ≥ 80 % on new code.
- [ ] **Integration**: provider tested against its real service (Testcontainers) or its fixtures (WireMock); it passes the **contract suite** of each of its capabilities; persistence green on SQLite and PostgreSQL.
- [ ] **E2E**: at least one end-to-end Playwright journey for the main feature; global smoke still green.
- [ ] **UI**: [DESIGN.md §10](DESIGN.md) checklist followed, screenshots attached.
- [ ] **Demo mode**: the slice provides realistic simulated data.
- [ ] **Documentation**: provider page (collector installation, minimum permissions, configuration), spec updated if a behavior changed, ADR if a decision was made.
- [ ] **Translations**: `en` and `fr` keys.
- [ ] **Security**: minimum permissions documented, secrets encrypted, no personal values (gitleaks + denylist green).
- [ ] **Dogfooding**: deployed on the preview instance and validated against at least one real case from the private inventory (§1.3).
- [ ] **Acceptance**: approved by the maintainer on the preview instance.

### 1.2 Git workflow

The repository follows the organization-wide GitHub template conventions:

- **Protected branches** (GitHub Rulesets, managed as code): `main` holds stable releases, `develop` is the integration branch. Nobody can push to them directly; every change goes through a pull request.
- **One GitHub issue per vertical** (template "Vertical slice"), then one branch from `develop`: `feature/<issue>-<short-description>` (e.g. `feature/42-reverse-proxy-routes`). Other prefixes: `fix/`, `chore/`, `docs/`.
- Commits follow **Conventional Commits** (`feat(routes): …`, `test(docker): …`).
- The PR targets `develop` and uses the repository template: summary, covered features, DoD checklist, screenshots, test notes.
- Mandatory CI: lint, unit tests, integration on both databases, E2E smoke, SonarQube, Trivy, gitleaks.
- Code review, then **squash merge** into `develop`.
- **Releases**: a release PR `develop → main` (1 approving review). semantic-release publishes the version, the CHANGELOG and the image from `main`; `develop` publishes pre-releases (`-beta.N`).
- A PR rarely exceeds ~1,500 lines excluding tests and fixtures. Beyond that, the vertical is split into stacked PRs (backend → API → UI), each one green.

### 1.3 Development and acceptance loop

A vertical goes through two phases. The first implementation is assumed to be wrong until the maintainer has tried it, so nothing is committed, pushed or run in CI while the product is still being shaped.

| Instance | Runs | Purpose |
|---|---|---|
| **sandbox** | the developer's working tree: `dotnet watch` for the API, the Vite dev server (hot reload) for the UI | exploration and acceptance, before any commit |
| **dev** | the `develop` image published by CI after each merge | what is integrated, used day to day |

Both run on the maintainer's development machine (resource-limited), connected **read-only** to a real environment. Their scripts and settings live in the maintainer's private configuration repository, never in this one. **Only integrated code (`develop`, later releases) produces an image in the registry.**

**Phase 1: exploration, in the sandbox.**

1. **Issue** (template "Vertical slice") with acceptance criteria taken from the spec's features, and an **acceptance plan**: the real cases that will prove the slice works.
2. **Develop** in the sandbox, on the working tree: no commit required, no push, no pull request, no CI, no image. The developer checks it runs and writes the tests that help development, not yet the full set.
3. **Acceptance.** At the end of the vertical, the maintainer gets the sandbox URL and what to try. Requested changes are made live (hot reload) until the maintainer accepts the product.

**Phase 2: industrialization, only for an accepted vertical.**

4. **Harden**: complete the unit, integration and E2E tests, lint and format, check the production build (security headers, served UI) once, write clean commits.
5. **Pull request**: it runs the whole pipeline (lint, tests, SonarQube gate, CodeQL, the three AI review agents). The author fixes what it finds and adds the verification report (each criterion, what it was checked against, the result, known limitations; no real infrastructure values) and the review section. The maintainer is given the pull request link.
6. **Merge** (squash) by the maintainer. CI then publishes the `develop` image and the dev instance is updated.

### 1.4 Order and dependencies

```
Phase 0 Foundations ─► Phase 1 Core (C1 → C7) ─► v0.1
                                   │
                                   ├─► V1 Docker ─► V2 Checks ─► V3 Routes ─► V4 Kubernetes ─► V5 Overview + Issues      ─► v0.2
                                   ├─► V6 Notifications + Heartbeats ─► V7 Hypervisor                                      ─► v0.3
                                   ├─► V8 Prometheus ─► V9 Loki ─► V10 Data ─► V11 Backups                                 ─► v0.4
                                   ├─► V12 Maintenance (registries, GitHub, end of life)                                    ─► v0.5
                                   ├─► V13 Security (Trivy, IdP)                                                           ─► v0.6
                                   └─► V14 Tool configuration ─► V15 IaC for Portarr (Terraform provider, Helm chart)      ─► v0.7
```

This order follows the spec's roadmap (§13). It may be adjusted after each release, based on dogfooding.

#### Maintainer priorities (2026-10-30)

Dogfooding reordered the roadmap around four tabs, each answering one question of the operator:

| Tab | Question | Verticals |
|---|---|---|
| **Apps** | "Where is everything I use, and does it work?" | V1 (done), V2 checks |
| **Infrastructure** | "Is any machine struggling, and what consumes it?" | V2 (one page: live CPU and memory per machine, then per container or pod; Kubernetes pulled forward from V4) |
| **Data** | "If everything crashes, can I get my state back?" | V10 + V11, merged and pulled forward |
| **Maintenance** | "What is vulnerable, outdated or late in my fleet?" | V12, pulled forward |

Resulting order after V2: **V10/V11 Data and backups → V12 Maintenance → V16 AI → the rest** (V3 Routes, V5 Overview, V6 Notifications…).

- **Data and backups** (V10 + V11): which databases and volumes exist, which are backed up, by which scheduled job, to which destination (local RAID array, S3), and when it last succeeded; everything that is *not* backed up is listed first. Portarr observes and reports (D-43); it does not run backups. Spec page: [verticals/V3-data-backups.md](verticals/V3-data-backups.md) (D-60 to D-63 for the first demo, superseded by D-77 to D-82; the page takes the next number, the V3 Routes row below keeps its place). The first slice discovered data from storage; the maintainer rejected it as too generic, and the accepted plan (step 4) made data declarative.
- **Maintenance** (V12): Dependabot and code scanning alerts across the operator's repositories, container images with a newer version, end-of-life runtimes, all in one view. Spec page: [verticals/V4-maintenance.md](verticals/V4-maintenance.md) (D-64 to D-67; the page takes the next number, the V4 Kubernetes row below keeps its place). First slice: GitHub connector (`sources: github:`), anonymous registry lookups, Maintenance page with a "needs attention" list and two sub-tabs (*My code*, *Deployed images*); end of life, declared versions and deployments follow.
- **V16 AI** (new, resolves Q-32): an MCP server and a knowledge base so that an AI assistant can learn the infrastructure and what is happening on it (inventory, statuses, issues, history). Read-only by default; any action stays out of scope until decided (D-31). Spec page: [verticals/V5-ai-mcp.md](verticals/V5-ai-mcp.md) (D-68 to D-71; the page takes the next number, the V5 Overview row below keeps its place). First slice: seven read-only tools over the existing views, two Markdown resources (infrastructure map, glossary), endpoint off by default behind an API key, Assistant page; issues and history come with V5 Overview and V6.
- **Apps layout** (new, small): the operator arranges the Apps page (columns, order and placement of stacks) instead of a fixed sequence.

#### Accepted plan after the demos (2026-10-31)

Portarr is a tool that uses other tools: each capability names **one recommended companion tool**, documented in [companions.md](companions.md) (what it is for, how to install and configure it, what Portarr reads from it), with generic deployment examples in `deploy/companions/` (Compose and Kubernetes manifests). The maintainer's own IaC deploys them where they are needed.

| Capability | Companion tool | Portarr reads |
|---|---|---|
| Host metrics (CPU per core, iowait, memory, swap) | **node_exporter** (container on Docker hosts, DaemonSet on Kubernetes) | `/metrics` every second, pushed live to the browser |
| Container and pod usage | Docker Engine API (socket proxy), metrics-server | stats, metrics.k8s.io |
| Backups of hosts and Docker VMs | **Backrest** (restic): plans, schedules, database dump hooks, local array + S3 repositories | its API: plans, last run, result, snapshots |
| Backups in Kubernetes | **K8up** (restic operator): schedules, PVC backups, annotation-based database dumps | its custom resources |
| Code and images | GitHub API, OCI registries | alerts, releases, tags |
| AI assistants | Portarr's own MCP server; agent CLIs in the optional `portarr-agent` container | — |

Steps, each accepted in the sandbox before the next:

1. **Infrastructure on host truth**: node_exporter read every second and streamed (SSE), overall load as the host sees it, per-core bars, iowait, swap bar, Unraid-like memory ring; node_exporter DaemonSet in Kubernetes so nodes are live too.
2. **Maintenance**: clickable summary chips (filters), "updated N min ago" and a refresh action.
3. **API documentation**: OpenAPI document and an interactive reference page (`/docs`).
4. **Declarative data** (done in the sandbox, D-77 to D-82): the operator declares in the catalog which apps' data are followed, their importance (accepted loss window) and their targets (local, S3); Portarr compares this policy with what the backup tools report. Backrest and K8up connectors; their deployment in the maintainer's IaC.
5. **Writable catalog** (done in the sandbox, D-83 to D-86): the catalog file stays the source of truth (D-44); the API edits it (validated, atomic write, previous version kept, comments preserved). The model gains descriptive knowledge for AI assistants: notes, access (how to reach a machine, never secrets), network links (e.g. a WireGuard tunnel between two nodes), external providers. MCP write tools, enabled separately and audited. If the single file grows too large, it may be split into several files; a database is not planned.
6. **Built-in chat** (done in the sandbox, D-87 to D-90, [V6](verticals/V6-chat.md)): an optional `portarr-agent` container runs an agent CLI headless (Claude Code first, through an adapter so other CLIs can follow); a React side panel reproduces an IDE chat (history, paste, model and agent choice, tool calls). Its only tool is Portarr's MCP server: it describes and edits Portarr's configuration with the operator's confirmation; it has no shell and no access to the machines.

#### The constellation (noted 2026-10-31)

Portarr is the first of three products that plug into each other through their APIs and MCP servers, each with one job:

| Product | Job | Relation to Portarr |
|---|---|---|
| **Portarr** | See and describe: the fleet, its apps, data and maintenance | — |
| **Orchestratarr** (below) | Act: orchestrate agents that fix, update and verify | reads Portarr's findings, acts through its own powers |
| **A memory product** (name to be chosen) | Remember: a long-term knowledge base for people and AI assistants (notes, decisions, runbooks, session summaries), searchable by meaning | registered in Portarr as a companion; Portarr's MCP server points assistants to it, so any assistant connected to Portarr finds the operator's memory too |

The memory product replaces today's ad-hoc setup (a Markdown wiki, a vector store and a document database that were never maintained as one project). Portarr does not store that memory (D-03): it links to it.

#### Future product: Orchestratarr (noted, not planned)

Once the fleet is observable, the operator will want to act on it. That is **not Portarr's job** (D-31): a separate brick, plugged into Portarr's API, would run an **orchestrator agent** with real powers, coordinating worker agents to fix a vulnerable dependency, update a container, repair an application, and verify that nothing regressed — automated maintenance for a growing number of apps. Portarr provides the knowledge and the findings (Maintenance, Data, Infrastructure); Orchestratarr acts.


---

## 2. Phase 0 — Foundations

**Goal**: a clean public repo in which a "hello world" feature goes through all three test levels and CI, before writing any business logic.

| PR | Content | Expected tests |
|---|---|---|
| **P0.1 Decisions** | ADRs 0001 to 0006: *arr-like stack (D-02 to D-06), license GPL-3.0 (D-27), language (Q-02), Dapper + FluentMigrator (D-28), xUnit v3 + NSubstitute + AwesomeAssertions (D-29), monorepo structure | — |
| **P0.2 Skeleton** | .NET solution (`Portarr.Common`, `.Core`, `.Http`, `.Api.V1`, `.Host`, test projects), Vite/React/TS frontend, `Directory.Build.props`, `global.json` (.NET 10), lint and format | Green build |
| **P0.3 Sample endpoint** | `GET /api/v1/system/status` (version, uptime) + minimal *System → Status* page | **1 unit + 1 integration + 1 E2E**: the pyramid runs end to end |
| **P0.4 CI/CD** | GitHub Actions: lint, tests (SQLite/PostgreSQL matrix), E2E smoke, SonarQube, Trivy, gitleaks + denylist, multi-arch image build, semantic-release, publishing to GHCR | Green CI on the PR |
| **P0.5 Repo hygiene** | README (pitch, demo mode screenshots), CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, issue and PR templates, `CLAUDE.md`, Dependabot | — |

---

## 3. Phase 1 — The core (v0.1)

| PR | Content | Features | Key tests |
|---|---|---|---|
| **C1 Host and persistence** | Configuration (file + `PORTARR__*`), logging, SQLite/PostgreSQL database, migrations, database backup and restore, `/health/live` and `/ready`, `/metrics` | F-SYS-01/04/05/06, NF-07/08 | Migrations replayed on both databases; restoring a backup |
| **C2 Authentication and roles** | Local accounts, generic OIDC, Admin / Operator / Observer / Guest roles, scoped API keys, audit log | F-SET-05, §11.2 | Role × endpoint matrix in integration; E2E local login + OIDC (containerized Keycloak) |
| **C3 Frontend shell** | `Page`, `PageHeader`, `PageSidebar`, `PageToolbar`, `PageContent*`, dark/light themes + `data-color-impaired`, `translate()`, routing, API client (TanStack Query), `SignalRListener`, base components (DESIGN §4.1) | F-SET-06, NF-09/10 | Vitest + axe on every component; visual regression of the shell |
| **C4 Provider framework** | Equivalent of `ThingiProvider`: definitions, `[FieldDefinition]` schema, CRUD, *Test* action, secret encryption, per-provider health status; Settings → Providers UI (cards, generated Add/Edit); **contract suites** per capability | §7, F-SET-02 | Generated form tested from a schema; secret never returned by the API |
| **C5 Domain and engine** | Environment / Runtime / Workload / App / Stack / Check / Issue / Event; status engine (roles, viability, anti-flapping); event bus; command queue and scheduled tasks; System → Health / Tasks / Events | §4, §5, F-SYS-02/03 | The bulk of the project's unit tests |
| **C6 Catalog and launcher** | `catalog.yaml` + JSON Schema + hot reload, CRUD UI (read-only when managed by config), provenance, `AppIcon` (dashboard-icons), Apps index (Posters / Overview / Table, sorting, filters, options), app detail (skeleton), Ctrl+K search | F-CAT-01/02/03/04/06/07, F-SET-01/03 | E2E: create an app via the UI, then via YAML; search |
| **C7 Demo mode** | Simulated provider covering every capability, scenarios (outages, CVEs, updates) | F-DEMO | Serves as the foundation for visual E2E tests |

**v0.1 release**: Portarr installable via `docker run`, complete launcher, authentication, provider framework ready, online demo possible.

---

## 4. Phase 2 — The vertical slices

Each row is a PR (or a stack). The listed tests come on top of the generic DoD.

### v0.2 — See

| Vertical | Content | Features | Integration | E2E |
|---|---|---|---|---|
| **V1 Docker** | Docker provider (socket-proxy): inventory, state, native healthcheck, real-time stats, live logs, label-based provenance, inferred dependencies, abandoned workloads; Infrastructure → Workloads | F-HLT-03, F-INV-01/02/03/05/06, F-LOG-01/02, F-MET (real time) | Healthy / `unhealthy` / crash-looping sample containers via Testcontainers; read-only socket-proxy rejects writes | Live logs of a container; `unhealthy` workload visible |
| **V2 Checks** | HTTP, TCP, DNS, ping probes; TLS; history (`UptimeBar`), uptime %; automatically suggested checks | F-HLT-01/02/04/07/11 | Sample HTTP server alternating 200/500; certificate close to expiry | Accept a suggested check, see it turn red |
| **V3 Routes** | Reverse proxy provider (SWAG/nginx first, several chained proxies, D-42): routes, resolution to workloads, SSO, anomalies | F-NET-01/02/03 | Corpus of anonymized configs (3 upstream styles) | An app gets its URL from a route |
| **V4 Kubernetes** | Kubernetes provider: nodes, namespaces, workloads, pods, state and reasons, live logs, metrics-server | D-13, F-HLT-03, F-INV, F-LOG-01 | **k3s** via Testcontainers: pod in `CrashLoopBackOff`, deployment 2/3 | See a failing pod and its logs |
| **V5 Overview and Issues** | Tiered Overview screen (E-00), issue engine (deduplication, severity, snooze), Activity | E-00, F-ISS-01, F-ACT-03, F-HLT-05 | A root cause groups child issues | Container outage → verdict and issue in the Overview |

### v0.3 — Get notified

| Vertical | Content | Features | Integration | E2E |
|---|---|---|---|---|
| **V6 Notifications and heartbeats** | Notification providers (email first), per-trigger rules, toasts, Web Push (PWA); push-mode heartbeats; maintenance | F-NOT-01/02/04, F-HLT-06/08 | Mailpit receives the alert, then the recovery; missed heartbeat → `Down` | Receive a toast; declare a maintenance window |
| **V7 Hypervisor** | Unraid provider (D-40), outside view of VMs, environment hierarchy, basic capacity | D-11, F-INV-01, F-MET-02 (allocation) | Simulated hypervisor API (WireMock) | Hypervisor → VM → runtime tree |

### v0.4 — Resources and data

| Vertical | Content | Features | Integration | E2E |
|---|---|---|---|---|
| **V8 Prometheus** | Canonical metrics and query templates, sparklines, history, **"who uses what" explorer**, capacity and overcommitment, Prometheus discovery endpoint | F-MET-01/02/05, F-API-06 | Containerized Prometheus with injected series | Treemap: clicking a pod opens its panel |
| **V9 Loki** | Historical logs, links to Grafana, **unexpected shutdowns and probable cause**, SSH logins, tunnels | F-LOG-03, F-HLT-09/10, F-SEC-02 (SSH) | Containerized Loki + injected `oom-kill` lines | Unexpected shutdown explained |
| **V10 Data** | Database inventory, storage, saturation forecasting | F-DAT-01/02, F-MET-04 | Sample Postgres and InfluxDB + exporters | Data → Databases |
| **V11 Backups** | Policies, S3 verification, co-location, coverage (observe only, D-43; tool chosen at start, Q-50) | F-BAK-01/02/03/04 | MinIO + dated objects; missed backup | An overdue backup becomes an issue |

### v0.5 to v0.7

| Vertical | Content | Features |
|---|---|---|
| **V12 Maintenance** | Registries (image versions), GitHub (Dependabot, SBOM, releases), endoflife.date, deployed / declared / published versions, deployments, code dependencies | F-UPD-01→07, F-SEC-04 |
| **V13 Security** | Trivy (image vulnerabilities), identity provider events | F-SEC-01/02 |
| **V14 Tool configuration** | Connectors that push configuration to their tool through its API (alert rules, scrape targets), with audit (D-36, D-37). No workload actions (D-31) | D-36 |
| **V15 IaC for Portarr** | `portarr` Terraform provider (apps, stacks, connectors as code) and Helm chart for Portarr itself. The collector kit is not shipped (D-38) | F-API-05 |

---

## 5. Blocking prerequisites

| Blocks | Question to settle |
|---|---|
| P0.1 | ✅ License, data access, test stack decided (D-27 to D-29); language (Q-02) |
| C2 | Final roles (Q-04), what an Observer sees (Q-46) |
| V3 | ✅ SWAG/nginx first, chained proxies (D-42) |
| V6 | ✅ Alerts evaluated by the tools (D-37) |
| V7 | ✅ Unraid first (D-40) |
| V11 | Backup tool (Q-50, ✅ restic with Backrest and K8up, [V3-data-backups.md](verticals/V3-data-backups.md)); role ✅ observe only (D-43) |

---

## 6. Tracking

- Each vertical slice has a **GitHub issue** ("Vertical" template) that restates its row from this plan and the DoD.
- One **milestone** per release (v0.1 … v0.7).
- A **GitHub project** (board): Backlog → Spec OK → In progress → In review → Merged → Validated in dogfooding.
