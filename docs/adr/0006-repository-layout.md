# 0006 — Repository layout

- **Status**: accepted
- **Date**: 2026-10-28
- **Spec**: D-35

## Context

Portarr is one product with a backend, a frontend and tests at three levels. Connectors must stay decoupled from the core.

## Decision

A single repository:

```text
src/                         .NET solution (Portarr.sln)
├── Portarr.Common/          cross-cutting helpers (build info…)
├── Portarr.Core/            domain, engine, capabilities; no ASP.NET dependency
├── Portarr.Providers.*/     one class library per connector (from V1)
├── Portarr.Http/            HTTP plumbing shared by API versions
├── Portarr.Api.V1/          controllers and resources for /api/v1
├── Portarr.Host/            entry point (assembly Portarr.dll)
├── Portarr.Test.Common/     CoreTest<T>, AutoSubstitute, builders
├── Portarr.Core.Test/       unit tests
└── Portarr.Integration.Test/ in-memory host (WebApplicationFactory), Testcontainers
frontend/                    React app (Vite root), built to _output/UI
tests/e2e/                   Playwright specs and the docker-compose e2e stack
docs/                        spec, design, testing, plan, ADRs
```

`package.json` sits at the root (like Sonarr); package versions are central (`Directory.Packages.props`).

## Consequences

- A connector is added as a new `Portarr.Providers.<Name>` project referencing only `Portarr.Core`.
- Loading connectors from external assemblies stays possible later at low cost.

## Amendment: the assistant lives in its own assembly (2026-11-01)

`Portarr.Mcp` holds the MCP server, the assistant's digest and knowledge base, the built-in chat and the `/api/v1/assistant` controllers that serve the Assistant page. That is a deliberate exception to "controllers live in `Portarr.Api.V1`": the assistant is one optional feature with its own options, rate limits, permission model (`[RequiresPermissions]`, D-94) and ASP.NET Core Application Part, and it references `Portarr.Api.V1` for the resources it re-reads. Moving its controllers would split the feature across two assemblies without making either simpler. What is not an assistant concern does not live there: the cross-domain "needs attention" classification is `IAttentionService` in `Portarr.Core`, so every surface (the MCP overview, a future Overview page, notifications) reads the same findings.
