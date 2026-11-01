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
- Core also takes decisions on runtime *names*: a `host` must be a docker or launcher-only environment, a Backrest or K8up data source is allowed or forbidden by `Runtime == kubernetes`, workload selectors accept `namespace` or `app` only for Kubernetes.

Adding a connector therefore means editing Core in several places, and Core grows with every connector. Nothing is broken for the operator; the cost is paid by every new connector, and it grows with the number of existing ones.

The catalog format itself is good: strict, readable, with messages that name the faulty entry. It must not change.

## Decision

1. **Core knows an environment generically**: id, name, address, host, notes, access, machines, plus `Connectors` (a section key to a typed object that Core never inspects) and `MachineHosts` (a machine of the environment to the environment it is a VM on, contributed by a connector, D-59).
2. **Each connector library registers an `IEnvironmentConnector`**, an interface of `Portarr.Core` (namespace `Portarr.Core.Catalog`), through one `AddSingleton<IEnvironmentConnector, …>` line in the library's `Add…Provider()` extension, like its other services. It declares the `Section` it owns (the YAML key: `docker`, `kubernetes`, `backrest`…) and the traits Core needs to decide without knowing the connector: `IsRuntime` (at most one per environment; `hostMetrics` and `backrest` are auxiliary) and `SingleMachine` (the environment is one machine: it can be a VM host, carry a Backrest, a host agent). `Parse(context)` validates the section: the context gives the environment id, its runtime, the other sections it declares, an error sink, and `Read<TDocument>()`, which deserializes the section strictly with the catalog's deserializer. A connector returns its typed settings and optionally `MachineHosts`.
3. **Strictness is kept**: the keys an environment accepts are the generic ones plus the registered sections; any other key is an error, as today, and a section nobody registered is such a key. Errors keep their wording and name the environment and the key; an error inside a section names the line of the catalog.
4. **The typed settings move to the library that parses them**, and its code reads them through an extension on `CatalogEnvironment` (`environment.Kubernetes()`), so the typed access lives where the type lives.
5. **Core code that branched on a connector goes through the seams it already has or through the traits**: host metrics targets are resolved by the host metrics sources (`IHostMetricsSource.TargetsOf(environment, sample)`), machine names and hosts come from `MachineHosts`, `Runtime` is the section of the one runtime connector present (else `none`), and the decisions on runtime names (which environment can host a VM, where a Backrest source can be read, how a machine is named) read `SingleMachine` instead of testing `kubernetes` or `cloudflare`.
6. **The parser receives the registered connectors**: `CatalogParser.Parse(yaml, connectors)`, and the services get an injected `ICatalogParser`. `Parse(yaml)` without connectors stays for the tests of the generic parts. Core tests that need a section use a stub connector from `Portarr.Test.Common`; each connector's syntax tests move, unchanged, to its own test project.
7. **The `reader:` shortcut stays catalog syntax** (D-102): the parser rewrites it into a `docker` and a `hostMetrics` section, which the two connectors then read like any other. The reader's own paths stay next to it, in Core.
8. **Security invariants are kept, shared and tested in every connector that sends a secret to a catalog URL**: a `tokenEnv` that makes Portarr send a variable's value to a URL must name a variable of the `PORTARR_READER_` namespace (D-102), and the Kubernetes API server is https-only unless `allowInsecure` (D-103). The namespace rule is one helper in `CatalogValues`, used by every connector that has a `tokenEnv`; each connector's moved test suite has the negative test (a variable outside the namespace is refused, `http://` without `allowInsecure` is refused). A slice is not done without them.
9. **Nothing changes for the operator**: same catalog format, same messages, same API, same behavior. The refactor is proved by moving the existing parser tests, not by rewriting them.

Out of scope here: the data side (a data asset's backup sources `backrest`, `k8up`, `cronjob`, the code source GitHub, the check kinds) has the same shape and follows the same pattern, in its own decision if the first slices show it is needed. `RuntimeNames` stays as plain strings while apps select a workload with `runtime@environment`. Which selector fields a runtime accepts is a workload seam (`IWorkloadSource`), handled in the last slice.

## Consequences

- A new connector is one library and one registration line, and Core stops growing, once the last slice has removed the decisions on runtime names (slice 6). Until then, a new runtime would still need the edit points that slice lists.
- Core can no longer read a connector's settings directly, which is the point; the places that did (monitors, knowledge base) are rewritten against the seams.
- The two services that read a catalog (`CatalogService`, `CatalogEditor`) take an `ICatalogParser`; the tests that declare a connector section pass the connector they need.
- Section errors lose nothing: the events of a section keep their positions, so an error inside one still names the line.

## Slices

Each slice is one pull request, with the whole suite green and the moved tests unchanged.

1. The mechanism in Core (connector interface, generic environment, parser built from connectors, stub connectors for the tests), with **Backrest** moved first: the most self-contained connector.
2. **Cloudflare**.
3. **Docker**, **hostMetrics** and the **reader** shortcut (`IHostMetricsSource.TargetsOf`, the shared token-namespace helper).
4. **Kubernetes**: settings, node exporter routes, `nodeHosts`, selectors' settings.
5. Cleanup: the typed fields and the `Runtime` switch leave `CatalogEnvironment`.
6. The decisions on runtime names: host target, data-source placement and machine naming read `SingleMachine`; workload selector fields move behind `IWorkloadSource`.

## Alternatives considered

- **Keep the typed records and split the parser into partial classes.** Cosmetic: Core still changes for every connector.
- **A base `ConnectorSettings` record in Core that connectors inherit.** Core would still need a discriminator and could not deserialize a subtype it does not reference; the registry of section keys is the simpler form of the same idea.
- **Load connectors as external assemblies.** D-35 says not for now; this decision keeps that door open without opening it.
