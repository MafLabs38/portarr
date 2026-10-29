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

A vertical is **accepted by the maintainer on a running instance before its pull request exists**. The product is validated first; the full pipeline only runs on what the maintainer has accepted, so no CI time is spent on a change that will be reworked anyway.

| Instance | Runs | Purpose |
|---|---|---|
| **preview** | the feature branch, built **from source** on the development machine | acceptance, before the pull request |
| **dev** | the `develop` image published by CI after each merge | what is integrated, used day to day |

Day-to-day development uses the usual inner loop (`dotnet watch`, Vite dev server, unit tests); no image is involved. Pull requests are built and tested by CI but never published: **only integrated code (`develop`, later releases) produces an image in the registry.**

Both instances run on the maintainer's development machine, connected **read-only** to a real environment. Their deployment files and settings live in the maintainer's private configuration repository, never in this one.

1. **Issue** (template "Vertical slice") with acceptance criteria taken from the spec's features, and an **acceptance plan**: the real cases that will prove the slice works.
2. **Develop** on a feature branch, with the fast local checks only (build, unit tests, resource-limited). No pull request, so no CI.
3. **Preview.** The author builds the branch on the development machine (resource-limited), starts it as the preview instance and checks the acceptance plan against the real environment.
4. **Acceptance.** The maintainer gets the preview URL with the exact steps to test and the expected results, and tests the product. Feedback goes back to step 2 on the same branch.
5. **Pull request**, opened only once the maintainer has accepted the preview. It runs the whole pipeline: lint, unit, integration and E2E tests, SonarQube gate, CodeQL, and the three AI review agents. The author fixes what they find (the preview is rebuilt if behavior changes) and adds the verification report (section "Verification on the preview instance": each criterion, what it was checked against, the result, known limitations; no real infrastructure values) and the review section.
6. **Merge** (squash) by the maintainer once the pipeline is green. CI then publishes the `develop` image and the dev instance is updated.

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
| V11 | Backup tool (Q-50); role ✅ observe only (D-43) |

---

## 6. Tracking

- Each vertical slice has a **GitHub issue** ("Vertical" template) that restates its row from this plan and the DoD.
- One **milestone** per release (v0.1 … v0.7).
- A **GitHub project** (board): Backlog → Spec OK → In progress → In review → Merged → Validated in dogfooding.
