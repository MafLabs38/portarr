---
name: Vertical Slice
about: A provider or capability delivered end to end (one PR, see docs/IMPLEMENTATION_PLAN.md)
title: "[VERTICAL] V__ — "
labels: vertical
assignees: ""
---

## Scope
<!-- Copy the vertical's row from docs/IMPLEMENTATION_PLAN.md -->

- Provider(s):
- Capabilities:
- Features (spec IDs):

## What to observe → which tool → how Portarr connects (D-18)
| What | Reference tool | Integration point |
|---|---|---|
| | | |

## Required tests
- **Unit**:
- **Integration** (service under Testcontainers / fixtures):
- **E2E** (user journey):

## Blocking questions
<!-- Q-xx that must be settled first -->
-

## Definition of Done
- [ ] Unit tests, ≥ 80 % coverage on new code
- [ ] Integration tests + capability contract suites green on SQLite and PostgreSQL
- [ ] E2E journey green
- [ ] UI checklist (DESIGN.md §10) + screenshots
- [ ] Demo-mode data
- [ ] Provider documentation
- [ ] Translations (`en`, `fr`)
- [ ] Spec / ADR updated
- [ ] Dogfooded on a real environment
