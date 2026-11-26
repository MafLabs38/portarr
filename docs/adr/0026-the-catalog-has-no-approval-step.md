# 0026 — The catalog has no approval step: the role decides

- **Status**: proposed
- **Date**: 2026-11-26
- **Spec**: D-134; supersedes the approval of D-123 and D-125 (their guardrails stay); amends D-93 (the Operator role); consistent with D-16 and D-31 (the catalog is Portarr's own configuration, not the infrastructure)

## Context

D-123 let the settings that say how Portarr reaches a host (`runtime`, every connector section, an app's `check`, an MCP server's `http` and `stdio`) be written, but not by anyone alone: a role holding `settings.manage` wrote them at once, anyone else (an assistant, a key holding only `catalog.write`) left a **pending change** that an administrator approved in Settings, Catalog.

The maintainer asked for it to be removed, for a reason that holds: an approval step brings no security. Whoever is connected, a person or an assistant through an API key, acts with a role. If that role may approve, a compromised session of it approves too; if it may not, the role alone already says so. What protects the infrastructure is the role-based access behind every endpoint and tool, not a button. Meanwhile the queue made the maintainer approve, one click at a time, what they had asked their own assistant to do.

The same reasoning showed a gap in the roles: the built-in Operator held `catalog.write`, so it could change Portarr's description of the fleet, and with the queue gone it would have changed where Portarr sends a credential. The Operator is meant to read the infrastructure without touching anything sensitive.

## Decision

1. **No approval step.** An edit is written at once or refused, never queued. The pending changes (`CatalogPendingStore`, the merge of an approval), `GET /api/v1/catalog/pending`, `POST /api/v1/catalog/pending/{id}/approve`, `DELETE /api/v1/catalog/pending/{id}`, the Pending tab of Settings, Catalog and `catalog.pending.json` are removed. An edit answers `applied`, `unchanged` or, as a dry run, `valid`; `pending` and HTTP 202 are gone. A `catalog.pending.json` left by an earlier version is no longer read; delete it.
2. **The role decides.** Whoever is connected acts with their role, a person or the API key of one, an assistant included (the MCP tools and the chat go through the same editor and the same rules):
   - **Admin** writes the whole catalog at once, how a host is reached included.
   - **Operator** reads every page and changes nothing of Portarr's configuration: `catalog.write` leaves the role, so it has no catalog editor and no Settings (the Settings entries already require `catalog.write`, `settings.manage` or `users.manage`).
   - Changing how a host is reached, or removing an item that holds such a setting, needs `settings.manage` on top of `catalog.write`. A role that holds `catalog.write` alone (a custom role, the deprecated shared keys `PORTARR__APIKEY` and `PORTARR__MCP__APIKEY`) is refused with HTTP 403 and the reason, and reads the items without those settings: who may change them reads them, nobody else does.
3. **What stays.** The guardrails of D-123 on every address a change sets (`http`, `https`, `tcp` or `unix`; no `user:password@`; no loopback, link-local, unspecified or metadata address), the catalog's validation, versions and audit trail (D-83, D-85).

## Consequences

- An assistant connected with an Admin's key declares a machine, an app's check or an MCP server in one call; the operator sees it in the catalog and in the audit trail, not in a queue. An assistant connected with an Operator's key reads and cannot change anything.
- An existing Operator user loses the catalog editor on upgrade (the built-in roles are defined in code). To let an assistant describe the fleet, give it a key of an Admin; to vary the rights, a custom role is created as before.
- The description of a built-in role is now read from the code, like its permissions, so that it follows such a change.
- CLAUDE.md rule 6 loses its sentence on the approval.
