# Architecture Decision Records

One file per structural decision, numbered in order: `NNNN-title.md`. Each ADR matches a `D-xx` row in the [spec](../SPEC.md#2-frozen-decisions).
An accepted ADR is never edited to change its decision: a new ADR supersedes it.

| ADR | Decision | Spec |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | — |
| [0002](0002-arr-like-stack.md) | *arr-like stack: one container, .NET 10 API, React frontend | D-02, D-04, D-05, D-06 |
| [0003](0003-license-gpl-3.md) | GPL-3.0 license | D-27 |
| [0004](0004-dapper-and-fluentmigrator.md) | Dapper + FluentMigrator for data access | D-28 |
| [0005](0005-backend-test-stack.md) | xUnit v3, NSubstitute, AwesomeAssertions on Microsoft.Testing.Platform | D-29 |
| [0006](0006-repository-layout.md) | Monorepo layout and one assembly per layer and per connector | D-35 |
| [0007](0007-text-level-catalog-editing.md) | Text-level catalog editing: comments kept, only changed nodes re-emitted | D-83 |
| [0008](0008-headless-agent-cli-behind-an-adapter.md) | A headless agent CLI behind an adapter, not a terminal | D-87, D-88 |
| [0009](0009-local-accounts-and-permission-based-roles.md) | Local accounts, cookie session and permission-based roles | D-92 to D-99 |
