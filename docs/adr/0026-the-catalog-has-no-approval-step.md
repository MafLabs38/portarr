# 0026 — The catalog has no approval step

- **Status**: proposed
- **Date**: 2026-11-26
- **Spec**: D-134; supersedes the approval of D-123 and D-125 (their guardrails stay); consistent with D-16 and D-31 (the catalog is Portarr's own configuration, not the infrastructure)

## Context

D-123 let the settings that say how Portarr reaches a host (`runtime`, every connector section, an app's `check`, an MCP server's `http` and `stdio`) be written, but not by anyone alone: a person holding `settings.manage` wrote them at once, anyone else (an assistant, a key holding only `catalog.write`) left a **pending change** that an administrator approved in Settings, Catalog. The reason was that a connection holds an address Portarr calls with a credential, so whoever writes it could send that credential elsewhere.

In use, the step costs more than it protects. The operator runs Portarr for themselves and gives their assistant a key so that it describes the fleet for them: every machine, app or check the assistant declares waits in a queue until the operator opens Settings and clicks Approve, which is a click on something they asked for. The operator asked for it to be removed. The control that matters is already elsewhere: who holds `catalog.write` (a role, a personal key the operator gives), what an address may be (the guardrails), and the audit trail of every edit.

## Decision

1. **No approval step.** Whoever may edit the catalog (`catalog.write`: a signed-in user, or the personal API key of one, an assistant included) writes every setting at once, how a host is reached included. An edit answers `applied`, `unchanged` or, as a dry run, `valid`; `pending` and HTTP 202 are gone.
2. **The queue is removed.** `GET /api/v1/catalog/pending`, `POST /api/v1/catalog/pending/{id}/approve` and `DELETE /api/v1/catalog/pending/{id}`, the Pending tab of Settings, Catalog, and `catalog.pending.json` are gone. A `catalog.pending.json` left by an earlier version is no longer read; delete it.
3. **What stays.** The guardrails of D-123 on every address a change sets (`http`, `https`, `tcp` or `unix`; no `user:password@`; no loopback, link-local, unspecified or metadata address), the catalog's validation, versions and audit trail (D-83, D-85), and the read rule: how a host is reached is shown to a person holding `settings.manage`, and left out of what an assistant or a key holding only `catalog.write` reads, an edit that leaves those keys out keeping them.

## Consequences

- An assistant declares a machine, an app's check or an MCP server in one call; the operator sees it in the catalog and in the audit trail, not in a queue.
- A key holding `catalog.write` can point a connector at another address with a credential variable the connector accepts. That is now the operator's trust in whoever holds that permission, as for every other edit; the guardrails still refuse the addresses that are never legitimate.
- CLAUDE.md rule 6 loses its sentence on the approval.
