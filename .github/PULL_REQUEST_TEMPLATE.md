<!-- The title is a Conventional Commit (feat(scope): …, fix(scope): …, chore(scope): …): once squash-merged it decides the next version (docs/releasing.md). -->
## Summary
<!-- Briefly describe your changes (1-3 bullet points) -->

-

Closes #

## Type of change
- [ ] Vertical slice (new provider or feature, see the implementation plan)
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Refactoring (no functional changes)
- [ ] Documentation update

## Spec references
<!-- Features, decisions or questions covered: F-XXX-nn, D-xx, Q-xx -->
-

## How has this been tested?
- [ ] Unit tests (backend and/or frontend)
- [ ] Integration tests (Testcontainers / WireMock), green on SQLite **and** PostgreSQL
- [ ] Provider contract test suites for every declared capability
- [ ] E2E tests (Playwright)
- [ ] Manual testing (demo mode and/or real environment)

## Verification on the preview instance
<!-- Filled once the branch runs on the preview instance (built from source).
     Never include real infrastructure values; screenshots come from demo mode only. -->
| Acceptance criterion | Checked against | Result |
|---|---|---|
|  | demo / real environment | ✅ / ❌ |

Known limitations:
-

- [ ] Accepted by the maintainer on the preview instance
- [ ] The review section below lists, per agent, what was fixed and what was declined with its reason

## Definition of Done (vertical slices)
- [ ] ≥ 80 % coverage on new code
- [ ] UI follows `docs/DESIGN.md` §10 checklist; screenshots attached (dark + light, 390 px)
- [ ] Demo-mode data provided
- [ ] Provider documentation (collector setup, minimal permissions, configuration)
- [ ] Spec / ADR updated if behavior or a decision changed
- [ ] Translation keys added (`en`, `fr`)
- [ ] Minimal permissions documented, secrets encrypted
- [ ] Dogfooded on a real environment

## Review
<!-- One line per AI review agent (tests, quality, security, architecture): what was fixed, what was declined and why. An architecture verdict of "no" blocks the PR until it is addressed or decided. -->
- tests:
- quality:
- security:
- architecture:

## Checklist
- [ ] My code follows the project's coding conventions (`CLAUDE.md`)
- [ ] I have performed a self-review of my code
- [ ] My changes generate no new warnings or errors
- [ ] No secrets, credentials or real infrastructure values (domains, IPs, host names) are committed

## Screenshots
<!-- UI changes: before / after -->
