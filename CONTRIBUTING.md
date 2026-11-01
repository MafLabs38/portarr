# Contributing to Portarr

Thanks for your interest! Portarr is at an early stage; the best way to help right now is to read the [specification](docs/SPEC.md) and open an issue with feedback.

## Ground rules

- **English** for code, comments, docs, commits, issues and pull requests.
- **No real infrastructure data** in anything you submit: no domain names, IP addresses, host names or tokens. Use `homelab.example`, `server-1`, `10.0.0.10`. CI rejects commits containing secrets.
- Be kind. See the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/).

## Workflow

1. Open (or pick) an issue. For a new provider or feature, use the **Vertical slice** issue template.
2. Branch from `develop`: `feature/<issue>-<short-description>`.
3. Commit with [Conventional Commits](https://www.conventionalcommits.org/): `feat(docker): add live log streaming`.
4. Open a pull request against `develop` and complete the PR template, including the Definition of Done.
5. CI must be green: lint, unit, integration (SQLite **and** PostgreSQL), E2E smoke, SonarQube, Trivy, gitleaks.

Direct pushes to `main` and `develop` are blocked for everyone.

## What a good PR contains

- Tests at the right level of the pyramid ([TESTING.md](docs/TESTING.md)).
- UI that follows the *arr patterns and theme tokens ([DESIGN.md](docs/DESIGN.md)), with screenshots.
- English and French translation keys for any new UI text.
- Documentation updates (provider page, spec, ADR when a decision is made).
- Demo-mode data for new features.

## Adding a provider

Providers follow the *arr `ThingiProvider` pattern: a settings class with `[FieldDefinition]` attributes (the UI form is generated from it), a factory, and one or more **capability** interfaces (`Inventory`, `State`, `Logs.Live`, `Metrics`…). Every capability comes with a contract test suite your provider must pass. See the spec, §7.

A connector that owns a section of an environment in the catalog (`backrest:`, later `docker:`, `kubernetes:`…) registers an `IEnvironmentConnector` in its `Add…Provider()` extension, validates the section with the helpers of `CatalogValues`, exposes its typed settings through an extension on `CatalogEnvironment` (`environment.Backrest()`) and inherits `EnvironmentConnectorContract` in its tests. Core never reads a connector's settings. See [ADR 0013](docs/adr/0013-connectors-own-their-environment-settings.md); the Backrest connector is the worked example.

## Development setup

Instructions will be added with phase 0 of the [implementation plan](docs/IMPLEMENTATION_PLAN.md).
