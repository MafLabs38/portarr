# 0005 — Backend test stack

- **Status**: accepted
- **Date**: 2026-10-28
- **Spec**: D-29

## Context

The first draft of the testing strategy used NUnit + Moq like Sonarr. The organization's .NET template uses xUnit. FluentAssertions 8 is no longer free. The .NET 10 SDK no longer runs xUnit v3 tests through VSTest.

## Decision

- **xUnit v3** with **NSubstitute** and **AwesomeAssertions**, all under free licenses.
- Tests run on **Microsoft.Testing.Platform** (`global.json` → `"test": { "runner": "Microsoft.Testing.Platform" }`); coverage with `Microsoft.Testing.Extensions.CodeCoverage`.
- `CoreTest<TSubject>` (in `Portarr.Test.Common`) keeps the *arr ergonomics: `Subject` is built with substitutes for its dependencies, `Mocker` gives access to them, `Clock` is a `FakeTimeProvider`.
- Integration tests carry `[Trait("Category", "Integration")]`.

## Consequences

```bash
dotnet test --solution src/Portarr.sln -- --filter-not-trait "Category=Integration" --ignore-exit-code 8   # unit
dotnet test --solution src/Portarr.sln -- --filter-trait "Category=Integration" --ignore-exit-code 8       # integration
```

Exit code 8 means a test project had no test matching the filter, which is expected since each project holds one kind of test.
