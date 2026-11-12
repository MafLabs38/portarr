# MCP secret stores

Your MCP servers need keys. Portarr never holds them ([mcp-servers.md](../verticals/mcp-servers.md), D-117): the catalog names a secret by its key in **your vault**, and the rendering gives the command the workstation runs to read it. The vault's own access control decides which machine reads which key. This page says, for each kind of store, what the command is, what the store must hold, and what to grant.

The commands below are the standard ones of each store's CLI; Portarr writes them, it never runs them. The Vault ones, and the policies below, were run on 2026-11-12 against a throwaway Vault 1.18 server (a KV version 2 engine at `kv`): a token holding only the policy for `kv/data/mcp/memory` read `kv/mcp/memory` through the provisioning command and was refused every other key; the prefix policy read both MCP keys and nothing outside it. The Azure ones were not run against a real vault: try them once by hand on a workstation before relying on them.

| Kind | `secretStore.kind` | Read with | A secret is named |
|---|---|---|---|
| HashiCorp Vault | `vault` | `vault kv get -field=value <prefix>/<key>` | `<prefix>/<key>`, for example `kv/mcp/memory` |
| Azure Key Vault | `azure-key-vault` | `az keyvault secret show --vault-name <vault> --name <prefix><key> --query value -o tsv` | `<vault>/<prefix><key>`, for example `myvault/mcp-memory` |

## HashiCorp Vault

- **Store each secret in the field `value`** of a KV secret under the prefix: `vault kv put kv/mcp/memory value=…`. The workstation reads `-field=value`.
- **Address**: the command sets `VAULT_ADDR` from `secretStore.address` before reading. The workstation must be logged in (`vault login`, or an auth method of the machine) with a token whose policy may read the keys it needs.
- **What to grant** (the keys the workstation's plan lists, nothing else). For a KV version 2 engine mounted at `kv`:

  ```hcl
  path "kv/data/mcp/memory" { capabilities = ["read"] }
  path "kv/data/mcp/github" { capabilities = ["read"] }
  ```

  or the whole prefix, `kv/data/mcp/*`, for a machine that may read every MCP secret.

## Azure Key Vault

- **The secret's name** is `secretStore.prefix` followed by the key, with letters, digits and dashes only (an Azure rule): with the prefix `mcp-`, the key `memory` is the secret `mcp-memory`. The vault's name is the first label of `secretStore.address` (`https://myvault.vault.azure.net`).
- The workstation must be signed in (`az login`).
- **What to grant**: the built-in role **Key Vault Secrets User**, assigned to the machine's identity on the vault, or on each secret for a narrower grant.

## On the workstation

- The commands are meant for a **shell profile** (`~/.profile`, `~/.zshrc`, a PowerShell profile): every shell then resolves the secret when it starts, and no value is ever written to a file by Portarr or by the assistant.
- A program started from a launcher rather than from a shell does not see these variables: start the client (Claude Code, Cursor, an editor) from a shell. On Windows, setting a variable for the user makes it visible everywhere but stores the value on that machine: it is your choice, and the renderings do not do it for you.
- The assistant that applies a plan runs the provisioning command as given, after your approval; it is told never to fetch, print or write a secret value itself.
