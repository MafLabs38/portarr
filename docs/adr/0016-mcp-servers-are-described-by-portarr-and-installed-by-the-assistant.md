# 0016 — MCP servers are described by Portarr and installed by the assistant

- **Status**: proposed
- **Date**: 2026-11-12
- **Spec**: D-110, D-112, D-117 (the vertical is [mcp-servers.md](../verticals/mcp-servers.md))

## Context

An operator uses several AI assistants on several machines and keeps re-describing how each MCP server is installed, client by client. Portarr already knows the operator's infrastructure and already runs an MCP server of its own (V5). The rules of the product stay: Portarr informs, the operator acts (D-16, D-31); Portarr stores no secret it does not need (rule 9).

## Decision

1. **Portarr describes, the assistant installs.** The catalog gets `mcpServers` and `workstations`; for a workstation and a client, Portarr renders the exact install operations (a command, or an entry to merge into the client's file) and the assistant on that machine applies them. Nothing in Portarr reaches a workstation (D-110).
2. **Only a person defines servers.** Editing the catalog from the MCP side goes through `ICatalogEditor.EditAsPersonAsync`, reached by the API only with a browser session holding `catalog.write`. API keys, the MCP tools and the chat cannot accept or dismiss a capture; an assistant can only *submit* what a machine has, into an inbox a person reads (D-112).
3. **Secrets are references.** A value is the name of a variable (`env:`) and, with a `secretStore`, a key in the operator's vault (`secret:`). Portarr never holds or reads a value; the plan gives the command that reads it on the workstation, in its own shell (D-117).
4. **The health is the server's, not the machine's.** A check type `mcp` (`initialize`; a refusal asking for credentials is healthy), run by its own monitor, with the state persisted (last healthy, down since). A local (stdio) server is not monitored (D-114).

## Consequences

- One renderer per client with golden files: a client whose format changes shows as a diff, and the nine supported clients are the only code that knows their formats.
- A new permission, `mcp.use` (Admin and Operator), and two persisted tables (health, captures) with migrations on SQLite and PostgreSQL.
- The vault side (policies, one identity per workstation) is the operator's, not Portarr's; the documentation describes the commands and Portarr generates them.

## Alternatives considered

- *Portarr installs on the machines (an agent per workstation).* Rejected: it would make Portarr act on the infrastructure (D-31) and hold a credential per machine.
- *An assistant defines servers directly in the catalog.* Rejected: an assistant would write configuration the operator never read (D-112); the inbox keeps a person in the loop.
- *Store the secret values in Portarr.* Rejected: Portarr would become a vault (rule 9, D-03).
