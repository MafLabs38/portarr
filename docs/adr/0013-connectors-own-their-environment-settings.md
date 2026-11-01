# 0013 — Connectors own their environment settings

- **Status**: accepted
- **Date**: 2026-11-01
- **Spec**: D-105 (completes D-35)
- **Backlog**: issue #22 item 14-1, step R1 of the correction plan in issue #29

## Context

D-35 says one class library per connector, so that adding a connector means adding a library. The catalog does not follow it. At the time of writing, Core knows each connector's settings:

- `CatalogEnvironment` has one typed field per connector (`Docker`, `Cloudflare`, `Kubernetes`, `HostMetrics`, `Backrest`), and `Runtime` is a closed `switch` over three of them.
- `Catalog.cs` and `DataDefinition.cs` hold the records of every connector (`KubernetesCluster`, `KubernetesNodeExporter`, `NodeExporterRoute`, `DockerEndpoint`, `CloudflareAccount`, `HostMetricsEndpoint`, `BackrestEndpoint`), `CatalogDocument.cs` holds the YAML shape of each, and `CatalogParser.cs` has one `Build…` method per connector.
- Core services branch on a connector: `HostMetricsMonitor.ResolveTargets` reads `environment.HostMetrics` and `environment.Kubernetes?.NodeExporter`, `InfrastructureMonitor` tests `environment.Kubernetes is null` to name a machine. The Mcp knowledge base and the fleet digest read settings too.
- Core also takes decisions on runtime *names*: the explicit `runtime:` key accepts three values, a `host` must be a docker or launcher-only environment, a Backrest or K8up data source is allowed or forbidden by `Runtime == kubernetes`, workload selectors accept `namespace` or `app` only for Kubernetes.

Adding a connector therefore means editing Core in several places, and Core grows with every connector. Nothing is broken for the operator; the cost is paid by every new connector, and it grows with the number of existing ones.

The catalog format itself is good: strict, readable, with messages that name the faulty entry. It must not change.

## Decision

1. **Core knows an environment generically**: id, name, runtime, address, host, notes, access, machines, plus `Connectors` (a section key to a typed object that Core never inspects) and `MachineHosts` (a machine of the environment to the environment it is a VM on, contributed by a connector, D-59).
2. **Each connector library registers an `IEnvironmentConnector`**, an interface of `Portarr.Core` (namespace `Portarr.Core.Catalog`), through one `AddSingleton<IEnvironmentConnector, …>` line in the library's `Add…Provider()` extension, like its other services. It declares the `Section` it owns (the YAML key: `docker`, `kubernetes`, `backrest`…) and, for a runtime, the traits Core needs to decide without knowing the connector: `IsRuntime` (at most one per environment; `hostMetrics` and `backrest` are auxiliary) and `SingleMachine` (the environment is one machine: it can be a VM host, carry a Backrest or a host agent). `Parse(context)` validates the section. The context gives the environment id, whether the environment is a single machine (true for an environment with no runtime connector, a launcher-only one, D-32), the other sections it declares, an error sink, and `Read<TDocument>()`, which deserializes the section strictly with the catalog's deserializer. An auxiliary connector checks "the environment is a single machine", never a runtime name. A connector returns its typed settings and optionally `MachineHosts`.
3. **Strictness is kept**: the keys an environment accepts are the generic ones plus the registered sections; any other key is an error, as today, and a section nobody registered is such a key. Errors keep their wording and name the environment and the key; an error inside a section names the line of the catalog.
4. **The explicit `runtime:` key stays**, and its allowed values are the sections of the registered connectors with `IsRuntime`; the "runtime must be …" message is built from them. A runtime declared without its section is handled by calling that connector's `Parse` with an empty section, so it reports what it needs, as it does today (`cloudflare.accountId is required`).
5. **The typed settings move to the library that parses them**, and its code reads them through an extension on `CatalogEnvironment` (`environment.Kubernetes()`), so the typed access lives where the type lives.
6. **Core code that branched on a connector goes through the seams it already has or through the traits**: host metrics targets are resolved by the host metrics sources (`IHostMetricsSource.TargetsOf(environment, sample)`, a member added to the existing interface), machine names and hosts come from `MachineHosts`, `Runtime` is the section of the one runtime connector present (else `none`), and the decisions on runtime names that concern an environment alone (which environment can host a VM, how a machine is named) read `SingleMachine` instead of testing `kubernetes` or `cloudflare`. Where a data source can be read is not one of them: K8up and CronJob sources need a Kubernetes cluster, Backrest sources only refuse one, which a single boolean cannot say; that placement check stays in the parser until the data side follows the same pattern (a hook next to `IBackupSource`).
7. **The parser receives the registered connectors**: `CatalogParser.Parse(yaml, connectors)`, and the services get an injected `ICatalogParser`. `Parse(yaml)` without connectors stays for the tests of the generic parts. Core tests that need a section use a stub connector from `Portarr.Test.Common`.
8. **The `reader:` shortcut stays catalog syntax** (D-102): Core validates it first, as it does today (the token is required and in the `PORTARR_READER_` namespace, the URL carries no credentials, query or fragment, the runtime is docker, it excludes `docker:` and `hostMetrics:`), then rewrites it into a `docker` and a `hostMetrics` section, which the two connectors read like any other. The existing reader parser tests stay in Core, unchanged, as the proof. The reader's own paths stay next to it, in Core.
9. **Connectors validate with the shared helpers**: URLs (an http(s) address without credentials), the name of a variable that holds a secret, blank values and the `PORTARR_READER_` namespace live in `CatalogValues` and are the only way a connector checks them.
10. **The security rules that exist today are kept, exactly, and tested in each connector they apply to**: the `PORTARR_READER_` namespace on `docker.tokenEnv`, `hostMetrics.tokenEnv` and `reader.tokenEnv` (D-102); the Kubernetes API server is https-only unless `allowInsecure` (D-103); Backrest and Cloudflare keep their variable-name rules (`userEnv` and `passwordEnv` both or neither). Known and unchanged, because changing it is an operator-visible decision of its own (ADR 0011 recorded it): the Kubernetes `tokenEnv` and the Backrest credential variables are not confined to a Portarr namespace, so someone who can write the catalog can point them at another secret and a URL. It is tracked in issue #45.
11. **`IEnvironmentConnector` is a capability and has its contract suite** (rule 8, `docs/TESTING.md` §3): `EnvironmentConnectorContract` in `Portarr.Test.Common` checks that a connector accepts its valid section, refuses an unknown key naming the line, names the environment in its errors, does not throw on an empty section, and, where its section has them, refuses a URL with credentials and a literal secret in a variable key. A section that holds a secret variable declares one that lies outside the `PORTARR_READER_` namespace: the suite checks that it is refused, or, for a connector that is not confined yet, that it is accepted and says so, pointing to #45, so that the day it is confined the test changes with it and a new unconfined secret is visible in review. Each connector's test project inherits it, and adds its own cases, moved unchanged from the parser tests.
12. **Nothing changes for the operator**: same catalog format, same messages, same API, same behavior. The refactor is proved by moving the existing parser tests, not by rewriting them.

Out of scope here: the data side (a data asset's backup sources `backrest`, `k8up`, `cronjob`, the code source GitHub, the check kinds) has the same shape and follows the same pattern, in its own decision if the first slices show it is needed. `RuntimeNames` stays as plain strings while apps select a workload with `runtime@environment`. Which selector fields a runtime accepts is a workload seam (`IWorkloadSource`), handled in the last slice, and so is the placement of a data source, which a hook next to `IBackupSource` will take over.

## Consequences

- A new connector is one library and one registration line, and Core stops growing, once the last slice has removed the decisions on runtime names. Until then, a new runtime would still need the edit points that slice lists.
- Core can no longer read a connector's settings directly, which is the point; the places that did (monitors, knowledge base) are rewritten against the seams.
- The two services that read a catalog (`CatalogService`, `CatalogEditor`) take an `ICatalogParser`; the tests that declare a connector section pass the connector they need.
- Section errors lose nothing: the events of a section keep their positions, so an error inside one still names the line.

## Slices

Each slice is one pull request, with the whole suite green and the moved tests unchanged.

1. The mechanism in Core (connector interface, generic environment, parser built from connectors, stub connector, `EnvironmentConnectorContract`), with **Backrest** moved first: the most self-contained connector. Flips this ADR to accepted.
2. **Cloudflare**.
3. **Docker** and the **reader** shortcut (rewritten into `docker` and `hostMetrics`), with the shared `PORTARR_READER_` helper.
4. **hostMetrics**: `IHostMetricsSource.TargetsOf` on the existing interface, implemented by the node_exporter source, with its case in `HostMetricsSourceContract`.
5. **Kubernetes**: settings, node exporter routes, `nodeHosts`, and its `TargetsOf`. Until it lands, `HostMetricsMonitor` keeps resolving cluster nodes itself.
6. Cleanup: the typed fields and the `Runtime` switch leave `CatalogEnvironment`; the `runtime:` key reads the registered runtime connectors.
7. The decisions on runtime names that concern an environment alone: host target and machine naming read `SingleMachine`; workload selector fields move behind `IWorkloadSource`. Data-source placement stays as it is (see decision 6).

## Alternatives considered

- **Keep the typed records and split the parser into partial classes.** Cosmetic: Core still changes for every connector.
- **A base `ConnectorSettings` record in Core that connectors inherit.** Core would still need a discriminator and could not deserialize a subtype it does not reference; the registry of section keys is the simpler form of the same idea.
- **Load connectors as external assemblies.** D-35 says not for now; this decision keeps that door open without opening it.
