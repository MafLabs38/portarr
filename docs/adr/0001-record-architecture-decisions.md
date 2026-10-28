# 0001 — Record architecture decisions

- **Status**: accepted
- **Date**: 2026-10-28

## Context

Portarr is built in public by one maintainer with AI agents. Decisions made in conversations get lost, and agents re-open questions that were already settled.

## Decision

Every structural decision is recorded as an ADR in `docs/adr/` (context, decision, consequences) and as a `D-xx` row in the spec. `CLAUDE.md` tells agents to read the spec's frozen decisions before any task.

## Consequences

- Questions marked ✅ in the spec's register are not asked again.
- Changing a decision means writing a new ADR that supersedes the old one.
