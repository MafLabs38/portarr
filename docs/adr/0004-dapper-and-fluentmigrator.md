# 0004 — Dapper + FluentMigrator for data access

- **Status**: accepted
- **Date**: 2026-10-28
- **Spec**: D-28

## Context

Portarr stores little: configuration, inventory, issue history. Metrics and logs stay in Prometheus and Loki (D-03). It must run on SQLite and PostgreSQL.

## Decision

- Queries with **Dapper** and explicit, parameterized SQL, behind repositories (like the *arr apps' `BasicRepository<T>`).
- Schema changes with **FluentMigrator** migrations, written once for both databases.
- No EF Core.

## Consequences

- Every query is visible and reviewable; no hidden N+1 or translation surprises.
- SQL differences between SQLite and PostgreSQL are handled in the repository layer, and every persistence test runs on both (CI matrix).
