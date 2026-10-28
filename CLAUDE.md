# Portarr

> Self-hosted homelab portal (app launcher + operations console) built in the spirit of the *arr suite (Sonarr, Radarr…).

This file is read automatically by Claude Code. It applies to every task in this repository.

## Read before any task

| Document | When |
|---|---|
| [docs/SPEC.md](docs/SPEC.md) | Always: frozen decisions (D-xx), domain model, features (F-xx), open questions (Q-xx) |
| [docs/DESIGN.md](docs/DESIGN.md) | **Before writing any UI code.** *arr patterns and theme tokens, to be followed strictly |
| [docs/TESTING.md](docs/TESTING.md) | Before writing or changing testable code |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | To know which vertical is being built and its Definition of Done |

## Tech stack

- **Backend**: .NET 10 (LTS), C# 14, ASP.NET Core, SignalR, FluentValidation
- **Data**: SQLite by default, PostgreSQL optional through **Dapper + FluentMigrator** (D-28)
- **Frontend**: React 18, TypeScript (strict), Vite, TanStack Query, zustand, CSS Modules, lucide-react, Chart.js
- **Tests**: xUnit v3 + NSubstitute + AwesomeAssertions (backend, D-29), Vitest + Testing Library + MSW (frontend), Testcontainers + WireMock.Net (integration), Playwright (E2E)
- **Quality**: .NET analyzers, `dotnet format`, ESLint, Prettier, Stylelint, SonarQube, Trivy, gitleaks
- **CI/CD**: GitHub Actions, semantic-release, multi-arch image on GHCR

## Commands

```bash
dotnet build src/Portarr.sln
dotnet test --solution src/Portarr.sln -- --filter-not-trait "Category=Integration" --ignore-exit-code 8   # unit tests
dotnet test --solution src/Portarr.sln -- --filter-trait "Category=Integration" --ignore-exit-code 8       # integration (Docker required from C1)
dotnet format src/Portarr.sln --verify-no-changes
dotnet run --project src/Portarr.Host                           # API on http://localhost:8484
yarn install && yarn start                                      # frontend dev server on :5173, proxies /api
yarn lint && yarn stylelint && yarn format:check && yarn typecheck && yarn test
yarn e2e                                                        # Playwright against a running Portarr (PORTARR_URL)
docker compose -f tests/e2e/stack/docker-compose.yml up -d --build   # the e2e stack
```

Test runner: Microsoft.Testing.Platform (ADR 0005). Exit code 8 = a test project had no test matching the filter (expected).

## Non-negotiable rules

1. **English only** for code, comments, documentation, commits, issues and PRs (D-26). UI text goes through `translate()` with English as the source language.
2. **Public repo, generic product.** Never commit real infrastructure values (domain names, IPs, host names, personal project names, tokens) in code, tests, fixtures, docs or commit messages. Use examples (`homelab.example`, `server-1`, `10.0.0.10`). gitleaks and the private deny-list block offending commits.
3. ***arr patterns first.** Page anatomy, components, prop vocabulary (`kind`, `size`), index / detail / settings / system screens follow Sonarr ([DESIGN.md](docs/DESIGN.md) §1–§5). Portarr is GPL-3.0 like Sonarr (D-27): code adapted from Sonarr/Radarr is allowed, with attribution in the file header.
4. **No hard-coded visual values**: design tokens only ([DESIGN.md](docs/DESIGN.md) §6). **No hard-coded UI strings**: `translate()`.
5. **Orchestrator, not a Swiss army knife** (D-03, D-30): integrate Prometheus, Loki, Docker, Kubernetes… Never store metrics or logs, never compute what a tool can compute (thresholds, aggregations, alert evaluation).
6. **Portarr informs, the operator acts** (D-16, D-31): no action on the infrastructure; the only write allowed is declarative configuration pushed to a tool through its API.
7. **One vertical = one PR**, with green unit, integration and E2E tests and a complete Definition of Done. **No PR is merged before the maintainer accepts it on the preview instance** ([IMPLEMENTATION_PLAN.md §1.3](docs/IMPLEMENTATION_PLAN.md)).
8. **One capability = one contract**: every provider passes the contract test suite of each capability it declares.
9. **Secure by default**: read-only access (docker-socket-proxy, minimal Kubernetes RBAC), secrets encrypted at rest and never returned by the API.
10. **Never** commit secrets. Use `dotnet user-secrets` locally and GitHub Secrets in CI.

## Backend conventions (C#)

### Naming

- **PascalCase**: types, methods, properties, events, namespaces
- **camelCase**: parameters, locals
- **_camelCase**: private fields
- **IPascalCase**: interfaces
- **PascalCase** for constants (.NET convention)

### Architecture

- One assembly per layer: `Portarr.Common`, `Portarr.Core`, `Portarr.Providers.*`, `Portarr.Http`, `Portarr.Api.V1`, `Portarr.Host`.
- Providers follow the *arr `ThingiProvider` pattern: definition + settings class with `[FieldDefinition]` + factory + capability interfaces.
- `TimeProvider` injected everywhere; never `DateTime.Now` in the Core.
- `async`/`await` for all I/O, `CancellationToken` propagated, no `.Result` / `.Wait()`.
- Nullable reference types enabled everywhere, **zero nullable warnings**.
- Parameterized queries only; validate every input (FluentValidation).

### SonarQube rules to watch

S2583/S2589 (always true/false conditions) · S3168 (async void) · S3776 (cognitive complexity ≤ 15) · S1481 (unused variables) · S1854 (dead stores) · S1066 (mergeable ifs).

## Frontend conventions

- One component per file + `*.module.css`; absolute imports from `src`.
- Server data through `useApiQuery` / `useApiMutation` (TanStack Query) only; screen options in zustand stores.
- Icons through `Helpers/Props/icons.ts`; props constants through `Helpers/Props` (`kinds`, `sizes`).

## Quality gates

- SonarQube: 0 bugs, 0 vulnerabilities, 0 critical code smells.
- Coverage: ≥ 70 % overall on Core, **≥ 80 % on new code**.
- All analyzer and lint warnings fixed.
- Details: [docs/TESTING.md](docs/TESTING.md) §6.

## Git workflow

Branches `main` (stable releases) and `develop` (integration) are protected by GitHub Rulesets: **no direct push, ever**, for anyone. Everything goes through pull requests.

- `main`: PR required, 1 approving review (org admins may bypass via PR only).
- `develop`: PR required.

```text
1. git checkout develop && git checkout -b feature/<issue>-<short-description>
2. Commit with Conventional Commits
3. git push origin feature/<issue>-<short-description>
4. gh pr create --base develop      (fill in the PR template, including the DoD checklist)
5. CI green + review → squash merge into develop
6. When develop is stable → release PR develop → main → semantic-release publishes the version
```

| Prefix | Usage | Example |
|---|---|---|
| `feature/` | New feature or vertical | `feature/42-docker-provider` |
| `fix/` | Bug fix | `fix/57-uptime-bar-overflow` |
| `chore/` | Tooling, dependencies, CI | `chore/61-bump-vite` |
| `docs/` | Documentation only | `docs/63-provider-guide` |

Conventional Commits: `feat(docker): …`, `fix(api): …`, `test(k8s): …`, `docs(spec): …`, `refactor(core): …`, `chore(ci): …`, `style(ui): …`.

Forbidden: direct push or force push to `main`/`develop`, merging without a PR.

## Decisions

Every structural decision gets an ADR in `docs/adr/NNNN-title.md` and a `D-xx` row in the spec.
