# 0016 — MCP servers are defined in Portarr and replicated onto machines, never the other way

- **Status**: proposed
- **Date**: 2026-11-12
- **Spec**: D-110 to D-116 (the vertical is [mcp-servers.md](../verticals/mcp-servers.md))

## Context

An operator uses several AI assistants on several machines and keeps re-describing how each MCP server is installed, client by client. Portarr is a configuration tool: the infrastructure and the apps of the portal are defined in its catalog, and what the catalog says is the truth. The rules of the product stay: Portarr informs, the operator acts (D-16, D-31); Portarr stores no secret it does not need (rule 9); tool-specific code stays out of the Core (D-36).

## Decision

1. **Portarr is the master, and the configuration flows one way.** An MCP server is defined once, in the catalog. Portarr renders from it the configuration of any client for any machine, and an assistant on the machine fetches it and applies it with the operator's approval. Portarr never reads what a machine really has, never imports it, never writes to a machine or reaches one, and keeps no state about one (D-110). A machine that differs from the catalog is brought in line.
2. **Only the catalog file defines servers.** The catalog edit API, the MCP tools and the chat cannot create, change or remove them, and every MCP tool is read-only (D-112).
3. **Secrets are references.** A value is the name of a variable (`env:`) and, with a `secretStore`, a key in the operator's vault (`secret:`). Portarr never holds or reads a value; the plan gives the command that reads it on the workstation, in its own shell (D-116).
4. **The health is the server's, not the machine's.** A check type `mcp` (`initialize`; a refusal asking for credentials is healthy), run by its own monitor, with the state persisted (last healthy, down since). A check Portarr could not run is Unknown, never Down (D-104). A local (stdio) server is not monitored (D-114). It is the only background work of the vertical.
5. **Narrow contracts, adapters in their own library.** `IMcpClientRenderer` (one AI client) and `IMcpSecretStoreProvisioner` (one kind of vault) each return only what is specific to their tool; the Core composes the generic parts (scope and operating system, required variables, the provisioning operation, the generic notes) in a rendering service, and finds the adapters through injected registries. A vocabulary interface gives the catalog parser the client ids and the store kinds, and the kind-specific checks of a prefix or a key belong to the provisioner, not to the parser. The nine renderers and the two provisioners are in `Portarr.Providers.McpClients`, which registers them in dependency injection like a connector does (D-113). Adding a client is one class and its registration.

## Consequences

- One renderer per client with golden files: a client whose format changes shows as a diff, and the supported clients are the only code that knows their formats.
- A new permission, `mcp.use` (Admin and Operator), and one persisted table (health) with migrations on SQLite and PostgreSQL.
- The vault side (policies, one identity per workstation) is the operator's, not Portarr's; the documentation describes the commands and Portarr generates them.

## Alternatives considered

- *Import a machine's configuration into the catalog (a capture inbox).* Rejected: it makes the machines a source of configuration and Portarr a reader of machines, the reverse of the principle (D-110), and it needs a write path from assistants and a review inbox.
- *Portarr installs on the machines (an agent per workstation).* Rejected: it would make Portarr act on the infrastructure (D-31) and hold a credential per machine (Q-65).
- *Store the secret values in Portarr.* Rejected: Portarr would become a vault (rule 9, D-03).
- *Keep the renderers in the Core behind a static list.* Rejected: it contradicts D-36 and makes every new client an edit of the Core.
