# Portarr — Test Strategy

> **Status**: v1, 2026-10-28. **Mandatory.** No vertical slice is merged without its three levels of tests (see [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)).
> **Reference**: Sonarr's test layout (`*.Test`, `Integration.Test`, `Automation.Test`), modernized.

---

## 1. The pyramid

```
                 ▲   E2E (Playwright)                          ~10 %   slow, few, user journeys
                ▲▲▲  Integration (API + real services)         ~20 %   Testcontainers, WireMock, real database
             ▲▲▲▲▲▲▲ Unit (backend + frontend)                 ~70 %   fast, isolated, all business logic
```

Placement rule: a behavior is tested **at the lowest level that proves it**. An E2E test does not verify a calculation rule (that is the job of a unit test); it verifies that a user sees the right result.

---

## 2. Unit tests

### 2.1 Backend (.NET)

| Topic | Choice |
|---|---|
| Framework | **xUnit v3** (D-29) |
| Mocks | **NSubstitute**, with a `CoreTest<TSubject>` base class that builds `Subject` and substitutes its dependencies, in the spirit of `CoreTest<T>` in Sonarr |
| Assertions | **AwesomeAssertions** (community fork of FluentAssertions 7, under a free license; FluentAssertions 8 changed its license) |
| Time | Injected `TimeProvider` (`FakeTimeProvider` in tests): no `DateTime.Now` in Core |
| Naming | `ClassUnderTestFixture`, methods named `should_<result>_when_<condition>` (Sonarr convention); integration tests carry `[Trait("Category", "Integration")]` |

**What must be covered by unit tests:**
- status engine and aggregation (app, stack, `critical`/`optional` roles, anti-flapping, maintenance);
- issue generation, deduplication, root cause;
- each internal health check (`HealthCheckBase`);
- translation of canonical metrics and PromQL/LogQL query templates;
- parsers: reverse proxy configs, provenance labels, versions and semver, end of life;
- provider settings validation (FluentValidation);
- API resource mapping;
- role-based authorization.

```csharp
public class StackStatusAggregatorFixture : CoreTest<StackStatusAggregator>
{
    [Fact]
    public void should_be_down_when_a_critical_member_is_down()
    {
        var stack = Builder.Stack()
            .WithMember("sonarr", MemberRole.Critical, Status.Down)
            .WithMember("recyclarr", MemberRole.Optional, Status.Healthy)
            .Build();

        Subject.Aggregate(stack).Should().Be(Status.Down);
    }
}
```

### 2.2 Frontend

| Topic | Choice |
|---|---|
| Framework | **Vitest** |
| Components | **React Testing Library** + `user-event` |
| Mocked API | **MSW** (Mock Service Worker), fed by the same fixtures as demo mode |
| Accessibility | `vitest-axe` on every shared component |

**To cover:** every component in `Components/` (states, `kind`/`size` variants, accessibility), data hooks (cache, invalidation on SignalR messages), formatters (bytes, cores, durations), the form generated from a provider schema.

---

## 3. Integration tests

The application **actually runs** and is driven through its public API, as in `Sonarr.Integration.Test`, but with real containerized dependencies.

| Tool | Usage |
|---|---|
| `WebApplicationFactory` | In-memory Portarr host, HTTP and SignalR client |
| **Testcontainers for .NET** | Real services: PostgreSQL, Docker (socket-proxy + sample containers), **k3s** (K3s module), Prometheus, Loki, Trivy, MinIO (S3) |
| **WireMock.Net** | Third-party APIs that cannot be containerized or have paid quotas: Backrest's Connect API, the Kubernetes API, node_exporter, the Docker `/info` endpoint, GitHub, registries, endoflife.date, hypervisor APIs, SMTP (or MailHog/Mailpit in a container). `ToolServer` in `Portarr.Integration.Test` wraps it: the connector under test runs with its real HTTP stack, on SQLite, with no Docker |

### 3.1 Integration test families

1. **Persistence**: each FluentMigrator migration is replayed on an empty database **and** on the previous version's database. The whole persistence suite runs on **SQLite and PostgreSQL** (CI matrix), like the *arr apps.
2. **API**: one fixture per resource (`/api/v1/apps`, `/stacks`, `/providers`, `/system/status`…). HTTP status codes, validation, role-based authorization, API key, pagination.
3. **Capability contract**: for each capability (`Inventory`, `State`, `Logs.Live`, `Metrics`…), an **abstract test suite** that every provider must pass. Adding a provider = inheriting from the suite and supplying its fake tool. This is what guarantees that the abstraction holds. The suites live in `Portarr.Test.Common/Contracts/`: `BackupSourceContract` (run by Backrest, K8up and Kubernetes CronJobs) and `HostMetricsSourceContract` (node_exporter over HTTP and through the Kubernetes pod proxy). The other capabilities still need theirs (workload inventory and logs, code repositories, image registries).
4. **Real provider**: each provider against its containerized service.
   - Docker: start an `unhealthy` container, verify state, live logs and provenance labels.
   - k3s: deploy a pod in `CrashLoopBackOff`.
   - Prometheus: load test series and verify the canonical metric.
5. **Real time**: a state change produces the right SignalR message.
6. **Notifications**: a transition to `Down` sends the expected email (Mailpit); anti-flapping is honored.

### 3.2 Recorded fixtures

Third-party API responses are stored under `tests/fixtures/<provider>/` (anonymized JSON). **No real infrastructure data**: gitleaks and the denylist apply to fixtures too.

---

## 4. End-to-end tests

| Topic | Choice |
|---|---|
| Tool | **Playwright** (TypeScript), Chromium + Firefox; WebKit in nightly |
| Target | The Docker image as actually built, started by `tests/e2e/stack/docker-compose.yml`: Portarr + socket-proxy + a few sample containers (one healthy, one `unhealthy`, one crash-looping) + Prometheus + Loki |
| Data | Real stack for operations journeys; **demo mode** for purely visual journeys |
| Pattern | Page Objects (`AppsIndexPage`, `ProviderSettingsPage`…) |
| Accessibility | `@axe-core/playwright` on every page visited |
| Visual regression | Playwright baseline screenshots of key pages, in dark and light themes: the safeguard for [DESIGN.md](DESIGN.md) |

**Reference journeys (smoke, on every PR):**
1. First start: setup wizard, admin creation, adding a Docker provider (*Test* button), workloads appear.
2. Creating an app and a stack from the UI, then via `catalog.yaml`.
3. A sample container turns `unhealthy`: the tile, the stack, the Overview and the Issues reflect the state in under 10 s.
4. Open a container's live logs and see a line emitted after opening them.
5. OIDC login (containerized Keycloak) with an `Observer` role: no action possible, no secret visible.

**Full journeys (nightly)**: one per vertical slice, defined in the implementation plan.

---

## 5. Other automated checks

| Check | Tool | When |
|---|---|---|
| Backend lint | .NET analyzers, `dotnet format --verify-no-changes` | PR |
| Frontend lint | ESLint, Stylelint, Prettier, `tsc --noEmit` | PR |
| Quality and coverage | SonarQube (Quality Gate) | PR |
| Static security analysis (SAST) | CodeQL (C#, TypeScript, workflows), `security-and-quality` queries | PR + weekly |
| AI review (advisory) | The four agents of `.claude/agents/` (tests, quality, security, architecture), one PR comment each, never blocking on their own: the author triages every finding (fixed, or declined with the reason) in the PR's Review section before asking for the merge | PR opened / ready, or `ai-review` label |
| Mutation testing | Stryker.NET (Core) and StrykerJS (frontend): do the tests catch injected bugs? Score and survivors in the job summary | weekly |
| Secrets and privacy | gitleaks + denylist of personal values | pre-commit + PR |
| Vulnerabilities | Trivy (image and dependencies), Dependabot | PR + daily |
| Dependency licenses | license scan (compatibility with the project license) | PR |
| Performance (optional) | k6 load test on the API, with 500 simulated workloads (NF-03) | release |

---

## 6. CI thresholds and rules

| Rule | Threshold |
|---|---|
| Core (.NET) coverage | ≥ 70 % overall, **≥ 80 % on new code** |
| Frontend `Components/` coverage | ≥ 70 % |
| Unit tests | 100 % green, duration < 3 min |
| Integration | 100 % green on SQLite **and** PostgreSQL |
| E2E smoke | 100 % green on every PR |
| Flaky tests | A flaky test is quarantined (`[Flaky]` tag) with an open issue; quarantine lasts no longer than 2 weeks |
| Visual regression | Any difference is explicitly approved in the PR |
| CodeQL | No high or critical alert on new code |
| Mutation score | Reported, not enforced: a surviving mutant in code a vertical relies on is a missing test, a survivor in glue code is not |

---

## 7. Directory layout

```
src/
├── Portarr.Core.Test/           # backend unit tests
├── Portarr.Api.Test/            # controller and mapper unit tests
├── Portarr.Providers.*.Test/    # unit tests per provider family
├── Portarr.Integration.Test/    # integration (WebApplicationFactory + Testcontainers)
└── Portarr.Test.Common/         # CoreTest<T>, builders, fixtures, FakeTimeProvider
frontend/src/**/__tests__/       # Vitest next to the components
tests/
├── fixtures/                    # anonymized third-party API responses
└── e2e/                         # Playwright: specs, page objects, docker-compose stack
```

---

## 8. Commands

```bash
dotnet test --solution src/Portarr.sln -- --filter-not-trait "Category=Integration" --ignore-exit-code 8   # backend unit tests
dotnet test --solution src/Portarr.sln -- --filter-trait "Category=Integration" --ignore-exit-code 8       # integration (Docker required)
yarn test                                                       # frontend unit tests (Vitest)
yarn e2e                                                        # Playwright against the e2e stack
(cd src/Portarr.Core.Test && dotnet tool restore && dotnet stryker)   # mutation testing, backend (~1 min)
yarn stryker run                                                # mutation testing, frontend (~15 min)
```

> Integration and E2E tests start containers and are CPU-intensive. On a small machine, prefer CI, or limit the test process's resources.

### Playwright against a running instance (no image build)

The E2E stack builds Portarr's image, which is too heavy for a small machine. The specs depend only on data any catalog has, so they also run against a running instance, a dev sandbox for instance:

```bash
PORTARR_URL=http://localhost:5174 yarn e2e --project=chromium --workers=1 specs/data.spec.ts   # …and the other specs
```

The specs of `launcher-and-workloads.spec.ts` and `logging-and-metrics.spec.ts` name the stack's own containers and only pass on the E2E stack; `/health` needs the API port (`PORTARR_URL` pointing at the Vite dev proxy answers the frontend instead).
