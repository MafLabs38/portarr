# 0002 — *arr-like stack

- **Status**: accepted
- **Date**: 2026-10-28
- **Spec**: D-02, D-04, D-05, D-06

## Context

Portarr follows the *arr applications (Sonarr, Radarr): self-hosted, simple to deploy, an API the UI consumes, extensible through providers. Their code base is a proven reference for this kind of product.

## Decision

- **One container**, serving the REST API and the built frontend on the same port (8484).
- **Backend**: .NET 10 LTS, ASP.NET Core controllers under `/api/v1`, SignalR for real time.
- **Frontend**: React 18, strict TypeScript, Vite, TanStack Query, CSS Modules, following Sonarr's frontend structure ([DESIGN.md](../DESIGN.md)).
- **Database**: SQLite by default, PostgreSQL as an option.
- The image is based on the chiseled ASP.NET runtime (no shell, non-root).

## Consequences

- `docker run` is enough to start Portarr; no external service is required (D-32).
- Unknown `/api` routes return 404; every other unknown route returns the frontend, which owns client-side routing.
