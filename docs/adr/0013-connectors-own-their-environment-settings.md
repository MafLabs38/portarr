# 0013 — Connectors own their environment settings

- **Status**: accepted
- **Date**: 2026-11-01
- **Spec**: D-105 (completes D-35)
- **Backlog**: issue #22 item 14-1, step R1 of the correction plan in issue #29

## Context

D-35 says one class library per connector, so that adding a connector means adding a library. The catalog does not follow it. Core knows each connector's settings:

- `CatalogEnvironment` has one typed field per connector (`Docker`, `Cloudflare`, `Kubernetes`, `HostMetrics`, `Backrest`): twelve constructor parameters, and `Runtime` is a closed `switch` over three of them.
- `Catalog.cs` holds the records of every connector (`KubernetesCluster`, `KubernetesNodeExporter`, `NodeExporterRoute`, `DockerEndpoint`, `CloudflareAccount`, `HostMetricsEndpoint`), `DataDefinition.cs` holds `BackrestEndpoint`, `CatalogDocument.cs` holds the YAML shape of each, and `CatalogParser.cs` (about 1,200 lines) has one `Build…` method per connector.
- Core services branch on a connector: `HostMetricsMonitor.ResolveTargets` reads `environment.HostMetrics` and `environment.Kubernetes?.NodeExporter`, `InfrastructureMonitor` tests `environment.Kubernetes is null` to name a machine. The Mcp knowledge base and the fleet digest read settings too.

Adding a connector therefore means editing Core in five places, and Core grows with every connector. Nothing is broken for the operator; the cost is paid by every new connector, and it grows with the number of existing ones.

The catalog format itself is good: strict, readable, with messages that name the faulty entry. It must not change.

## Decision

1. **Core knows an environment generically**: id, name, address, host, notes, access, machines, plus `Settings` (a section key to a typed object that Core never inspects) and `MachineHosts` (a machine of the environment to the environment it is a VM on, contributed by a connector, D-59).
2. **Each connector library registers an `IEnvironmentConnector`**: the `Section` it owns (the YAML key: `docker`, `kubernetes`, `backrest`…), whether it is a runtime (`IsRuntime`: at most one per environment, the others are auxiliary like `hostMetrics` and `backrest`), and `Parse(context)`. The context gives the environment id, the raw section, the other sections present (for rules such as "backrest runs on a docker host"), the catalog folder and an error sink. `context.Read<TDocument>()` deserializes the section strictly with the catalog's deserializer. A connector returns its typed settings and optionally `MachineHosts`.
3. **Strictness is kept**: the keys an environment accepts are the generic ones plus the registered sections; any other key is an error, as today. Errors keep their wording and name the environment and the key.
4. **The typed settings move to the library that parses them**, and its code reads them through an extension on `CatalogEnvironment` (`environment.Kubernetes()`), so the typed access lives where the type lives.
5. **Core code that branched on a connector goes through the seams it already has**: host metrics targets are resolved by the host metrics sources (`IHostMetricsSource.TargetsOf(environment, sample)`), machine names and hosts come from `MachineHosts`, and `Runtime` is the section of the one runtime connector present, else `none`.
6. **`CatalogParser` is built from the registered connectors** (injected). Core tests use stub connectors from `Portarr.Test.Common`; each connector's syntax tests move, unchanged, to its own test project.
7. **Nothing changes for the operator**: same catalog format, same messages, same API, same behavior. The refactor is proved by moving the existing parser tests, not by rewriting them.

Out of scope here: the data side (a data asset's backup sources `backrest`, `k8up`, `cronjob`, the code source GitHub, the check kinds) has the same shape and follows the same pattern, in its own decision if the first slices show it is needed. `RuntimeNames` stays as plain strings while apps select a workload with `runtime@environment`.

## Consequences

- A new connector is one library and one registration line; Core stops growing.
- Core can no longer read a connector's settings directly, which is the point; the places that did (monitors, knowledge base) are rewritten against the seams.
- The static `CatalogParser.Parse(yaml)` goes away; its 17 callers (tests and two services) receive a parser. This is the largest mechanical part of the work.
- Section errors lose nothing: today only YAML syntax errors carry a line number, and they are raised before any section is read.

## Slices

Each slice is one pull request, with the whole suite green and the moved tests unchanged.

1. The mechanism in Core (connector interface, generic environment, parser built from connectors, stub connectors for the tests), with **Backrest** moved first: the most self-contained connector.
2. **Cloudflare**.
3. **Docker**, **hostMetrics** and the **reader** shortcut (`IHostMetricsSource.TargetsOf`).
4. **Kubernetes**: settings, node exporter routes, `nodeHosts`, selectors.
5. Cleanup: the typed fields and the `Runtime` switch leave `CatalogEnvironment`.

## Alternatives considered

- **Keep the typed records and split the parser into partial classes.** Cosmetic: Core still changes for every connector.
- **A base `ConnectorSettings` record in Core that connectors inherit.** Core would still need a discriminator and could not deserialize a subtype it does not reference; the registry of section keys is the simpler form of the same idea.
- **Load connectors as external assemblies.** D-35 says not for now; this decision keeps that door open without opening it.
