# 0020 — The catalog is writable, and a change of connection waits for approval

- **Status**: proposed
- **Date**: 2026-11-16
- **Spec**: D-122, D-123, D-124 (amends D-16 and D-31)

## Context

Adding a machine, a VM and an app through the MCP was impossible. Three things stacked: D-16 and D-31 ("Portarr informs, the operator acts"), written for the infrastructure, were applied to Portarr's own description of it; the write path existed (D-83 to D-86) but was off by default, and off in production, where the catalog is a ConfigMap managed by GitOps; and the settings that say how a host is reached (`runtime`, the connector sections, an app's `check`) were never writable, although declaring a machine is exactly that. There was also no way to check a catalog without starting Portarr, and no screen: Settings only held the identity.

## Decision

- **Read-only toward the infrastructure, writable toward the configuration.** The catalog is written by three doors that share one editor (the same validation of the whole file, versions, backups, audit): the Settings, Catalog page, the API and the MCP tools. They all end in the same YAML file.
- **A change of connection is accepted, and waits for approval unless its author may reconfigure.** A person with `settings.manage` writes it at once. Anyone else (an assistant, a key with `catalog.write` only) leaves a pending change, stored next to the catalog, which an administrator approves or rejects in the UI. Approval merges the change key by key over the current item; it is a conflict only when the same key changed since.
- **Guardrails apply to everyone**: `http`, `https` or `unix`, no credentials in an address, no loopback, link-local or unspecified address literal (the outbound address policy of the registries and platforms lookups). The catalog holds no secret, as before.
- **A dry run** (`get_catalog_item_check`, `POST …/validate`) goes through every check and writes nothing; it is named like a read tool so that the chat runs it without asking (D-88).
- **The catalog lives on a volume Portarr writes**, seeded once from the operator's repository; `GET /api/v1/catalog/export` gives the file back. Git is a backup the operator keeps, not a requirement. Portarr does not open pull requests (it would hold a write token).

## Consequences

- Declaring a machine through an assistant is possible, with a human click for the part that is dangerous. A host name is not resolved at edit time: a name that points at an internal service is not caught by the guardrails, the approval is the control.
- A deployment that keeps its catalog in a ConfigMap stays read-only, and the page says so; it has to move to a volume to use the editor.
- Edits made in Portarr are not in the operator's repository until exported and committed.
- A pending change that is never approved stays until rejected (50 at most).

## Alternatives considered

- **Portarr opens pull requests on the repository.** Rejected: it contradicts D-16 and puts a repository write token in Portarr.
- **Keep connections out of the editor.** Rejected: it makes the main use of a portal of infrastructure impossible through an assistant.
- **Let any writer change a connection directly, behind guardrails alone.** Rejected: guardrails cannot know that a LAN address is the wrong one; an approval can.

## Addendum (2026-11-16): MCP servers and workstations

The same rule applies to `mcpServers` and `workstations` (D-125, which amends D-112): they are edited from Settings, the API and the MCP tools, and the keys that say how to reach or launch a server (`http`, `stdio`) wait for an administrator's approval when an assistant writes them. Banning the edit would have recreated, for these sections, the flaw this ADR corrects. `secretStore` stays in the file.
