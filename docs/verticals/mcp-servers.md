# MCP servers and workstations

> Status: **built, in review** (steps 1 to 4 in one pull request, after the sandbox; the maintainer's acceptance remains). D-110 to D-117 and ADR 0016 stay proposed (🟡) until the maintainer merges: freezing a decision is his. Features: F-MCP-01 to F-MCP-08. Decisions: D-110 to D-117. Builds on [V5](V5-ai-mcp.md) (Portarr's own MCP server, D-68 to D-71, D-86), [V7](V7-accounts.md) (accounts, per-user keys, permissions), the checks (D-50, D-51) and the catalog ([catalog.md](../catalog.md)).

## Need

> "I have many machines and every time I struggle to describe how my MCP servers are installed: I change assistant (Claude Code, Copilot, Cursor…), or I configured a server on one machine and forgot the others. I want a definition portal where I can install MCP servers easily by copying the configuration of my other machines, make them uniform, and know them, because I forget some. I used a memory MCP server once that was always down and I never noticed."

Two needs, in this order of importance:

1. **A portal of definitions.** Each MCP server is described once. Any assistant on any workstation can ask how to install it for its own client, and the configuration already present on one workstation can be *captured* into the portal instead of retyped.
2. **A health view of the servers.** An MCP server runs on a server and is expected to be up. Portarr checks the **server**, not the machines that use it, and remembers when it was last seen healthy, so a server that has been dead for two weeks is noticed.

## Goal

The catalog describes the MCP servers the operator uses and the workstations where assistants run. An assistant connected to Portarr fetches the exact configuration to install a server for **its own client** on **its own machine**, and can hand over what is already configured there. The operator reviews what was captured, accepts it into the catalog, and sees at a glance which servers are healthy and for how long.

## Vocabulary

| Term | Meaning |
|---|---|
| **MCP server** | Something an assistant connects to (Portarr's own `/mcp`, a memory server, a GitHub server…). Remote (`http`) or a local process (`stdio`). |
| **Client** | An assistant application with its own configuration file and format: Claude Code, Claude Desktop, Cursor, VS Code with Copilot, Windsurf, Gemini CLI, Codex CLI, OpenCode, Zed. |
| **Workstation** | A machine where the operator works with an assistant: a laptop, a training PC, a gaming PC, the dev VM. Only a **name** matters. Production machines are not workstations unless declared: nothing is installed on them by default. |
| **Wanted** | The servers the operator wants on a workstation. Declarative, nothing is verified. |
| **Capture** | A snapshot of the MCP servers configured in a client on a workstation, sent by an assistant to Portarr's inbox for the operator to review. |
| **Secret store** | The operator's vault (HashiCorp Vault, Azure Key Vault…). It holds the secret values and decides, with its own access control, which machine may read which key. |

## Scope

| In | Out |
|---|---|
| Catalog sections `mcpServers`, `workstations` and `secretStore`, validated like the rest of the catalog | Portarr writing to a workstation, running a command on it, or reaching it at all (D-110) |
| A menu entry **MCP**: servers, workstations, the capture inbox | Knowing whether a workstation is on, or what its files really contain (D-114) |
| Rendering a server for each supported client and scope: command and/or file change | An agent or companion on workstations |
| Health of remote servers, with "last healthy" kept across restarts (D-114) | Probing with credentials; local (`stdio`) servers are not checked |
| Capture: an assistant submits a workstation's configuration to an inbox; the operator reviews, merges duplicates and accepts (D-116) | An assistant defining servers directly (D-112) |
| Secret references to the operator's vault, with the command each workstation runs to read them (D-117) | Storing, reading, or handing out a secret value: Portarr holds none |

## Design

### Catalog (D-111)

```yaml
mcpServers:
  - id: memory                        # lowercase letters, digits and dashes
    name: Memory
    app: memory                       # optional: an app of the catalog (its URL and icon)
    transport: http                   # http or stdio
    http:
      url: https://memory.homelab.example/mcp    # http:// needs allowInsecure: true (as D-103)
      headers:
        X-Api-Key: { env: MEMORY_MCP_KEY, secret: memory }   # the variable, and the vault key behind it
    scope: user                       # default scope: user, project or local
    check: { interval: 5m, timeout: 10s }  # optional; interval 10 seconds to 24 hours (default 5 minutes), timeout 1 to 60 seconds and not longer than the interval; check: false disables it
    notes: Long-term memory.          # Markdown, same limits and secret check as D-84
  - id: github
    name: GitHub
    transport: stdio
    stdio:
      command: npx
      args: ["-y", "@example/github-mcp@1.4.2"]  # pin a version
      env: { GITHUB_TOKEN: { env: GITHUB_TOKEN, secret: github } }

workstations:                         # the machines you work from; a name is enough
  - id: dev-laptop
    name: Dev laptop
    os: windows                       # windows, linux or macos (commands differ, npx on Windows)
    clients: [claude-code, cursor]
    mcp:                              # what is wanted here
      - memory                        # shorthand: every client of the workstation, the server's scope
      - { server: github, clients: [claude-code], scope: user }
  - id: dev-vm
    name: Dev VM
    environment: server-1             # optional: this workstation is also this environment
    os: linux
    clients: [claude-code]

secretStore:                          # optional, see "Secrets"
  kind: vault                         # vault or azure-key-vault
  address: https://vault.homelab.example
  prefix: kv/mcp                      # where the MCP secrets live
```

- A value is a harmless literal or `{ env: NAME }`. A header or variable whose name suggests a credential (authorization, key, token, secret, password…) accepts only the second form. A literal that looks like a secret is refused by the heuristic of D-84 (private keys, token formats, `password=…`, credentials in URLs, long random strings), naming the field, never the value.
- `{ env: NAME }` is a variable **on the workstation**, read by the client. Portarr never reads it (unlike a connector's `tokenEnv`; D-108's namespaces do not apply). `secret:` is the key, under the store's `prefix`, whose value the workstation puts in that variable; it needs a `secretStore`.
- Validation errors (unknown server, client, environment or `app`, duplicate id, `secret:` without a `secretStore`) are shown like the other catalog errors; a broken edit keeps the last valid catalog.

### Rendering (D-113)

One renderer per client behind one contract (rule 8). Given a server, a scope and an operating system it returns **operations**. Each renderer is written from the client's documentation and records the version it was checked against ([connectors/mcp-clients.md](../connectors/mcp-clients.md)).

```jsonc
// get_mcp_server { server: "memory", client: "claude-code", scope: "user" } — illustrative shape
{
  "server": "memory", "client": "claude-code", "scope": "user",
  "requires": { "env": ["MEMORY_MCP_KEY"], "secrets": ["kv/mcp/memory"] },
  "operations": [   // kinds: command, file, provision; a removal plan uses kind "remove"
    { "kind": "command", "text": "claude mcp add --scope user --transport http memory https://… --header \"X-Api-Key: ${MEMORY_MCP_KEY}\"" },
    { "kind": "file", "path": "<client file>", "merge": { "mcpServers": { "memory": { "…": "…" } } } },
    { "kind": "provision", "text": "export MEMORY_MCP_KEY=\"$(vault kv get -field=value kv/mcp/memory)\"" }
  ],
  "instructions": "Show the operator exactly what you will run or write, wait for their approval, change nothing else in the file."
}
```

- A rendering contains no secret value, only the client's syntax for "read this variable". A client without one gets a placeholder and a sentence saying so.
- The supported clients are the main ones (the list in the vocabulary). Each is one renderer with golden-file tests, so adding one never touches the others.

### Health of the servers (D-114)

- Only **remote servers**, only **the server**: Portarr never asks whether a workstation is on, and a laptop switched off every evening is not an event.
- A new check type `mcp`, run by its own monitor (`McpHealthMonitor`, with the tick and the concurrency of the check scheduler, D-50): the state is richer than up or down (healthy, asking for credentials, down) and is persisted (last healthy, since), which the check results are not. One `initialize` request per interval (default 5 minutes; `check.interval` accepts 10 seconds to 24 hours), without credentials.

| Answer | State |
|---|---|
| A valid MCP `initialize` answer, a JSON-RPC error answer included (a server is there) | **Healthy** |
| 401 or 403 | **Healthy, authentication required**: the server is there and enforcing; nothing more can be said without a credential |
| Timeout, refused, DNS, TLS, 5xx, anything that is not MCP | **Down**, with the reason code of the checks |
| Portarr itself failed to run the check (a bug, not the server) | **Unknown**: what was known stays under it (state, since, last healthy), it is not blamed and is not listed as down (D-104); nothing is saved until a check that ran says something |
| A `stdio` server, or `check: false` | **Not checked** |

- The check history stays in memory like every check (D-51), but the figures that matter survive a restart in one small table: **last healthy**, **last checked**, **current state** and **since when**. The UI says "Healthy, seen 2 minutes ago" or "Down for 12 days, last healthy on 2026-10-30". A server that is Down is listed in the Overview's "needs attention" (finding `mcp-server-down`, with the time since) for callers who hold `mcp.use`: the point is to notice a dead server. No threshold and no notification: Portarr shows the duration and the operator judges (D-30).

### Workstations and the plan

- Workstations are declared by name; `mcp` lists what is wanted. `get_workstation_plan` returns, for a workstation: to **install**, the rendering of every wanted server for each of its clients; to **remove**, the removal operation of every server of the catalog that is not wanted there ("if present": the operator removes a server from a workstation by taking it out of the wanted list); and the secret keys the workstation must be able to read. Servers the catalog does not know are left alone. Every operation is shown to the operator and approved before it runs.
- There is **no installed state**. Whether the plan was applied is not tracked; the assistant that applied it is the witness.

### Capture and uniformization (D-116)

The way a catalog fills itself:

1. On a workstation, the operator says "capture my MCP configuration into Portarr". The assistant reads the client's configuration and calls `submit_mcp_capture { workstation, client, scope, servers: [...] }` with, for each server, its name, transport, URL or command and arguments, and the **names** of headers and variables. It is told never to send a value; the server also refuses any value the secret heuristic recognises, naming the field.
2. The capture lands in an **inbox** (database), never in the catalog. A hand-over holds at most 50 servers, and the inbox at most 500 servers waiting for a decision: past that a hand-over is refused until some are accepted or dismissed. A server that waits is never dropped.
3. In the **Captured** tab the operator sees each server compared with the catalog: *New*, *Same as `x`*, or *Differs from `x`* with the difference. Servers seen on several workstations under different names are grouped by an identity (the transport and the normalised URL, or the package without its version). The operator accepts as a new definition, merges into an existing one (the definition keeps one spelling for all), or dismisses.
4. Accepting writes the catalog through the editor (versioned, audited with the user and the channel `ui`), and adds the server to the capturing workstation's wanted list when the operator leaves that box ticked.

This is also how the operator rediscovers what they had installed and forgot.

### Secrets (D-117)

Portarr holds no secret and hands none out. The secrets live in the operator's vault; **its access control** decides which machine reads which key.

- The catalog names a secret by its key (`secret: memory`, under the store's `prefix`). The renderer produces, for the workstation's operating system, the **provisioning** command that puts the vault's value in the variable the client reads, resolved by the workstation's own shell when it starts (`export MEMORY_MCP_KEY="$(vault kv get -field=value kv/mcp/memory)"`; the Azure Key Vault equivalent uses the Azure CLI). The value never passes through the assistant or its provider.
- `get_workstation_plan` and the workstation page list the keys each workstation needs. The operator grants them in the vault (a policy, a role assignment), as for any other machine; Portarr does not check them.
- Without a `secretStore`, only `{ env: NAME }` exists and the operator provisions the variables as they like.
- One provisioner per store kind behind one contract (rule 8), written from the vault's CLI documentation and tested against golden outputs.

### Security (D-112)

- **A server definition is instructions an assistant will execute**: a `stdio` command runs code on a workstation, an `http` URL receives its headers. So a definition can be created or changed only by **a person**: in the catalog file, or in the UI with `catalog.write` and a **browser session** (as account and key management already require, D-94). An API key, an MCP tool or the chat cannot create, change or remove `mcpServers`, `workstations` or `secretStore`, and `get_catalog_item` leaves them out (the same principle as 17-1 in D-86).
- An assistant may only **propose** (the capture inbox) and **read**. A proposal never takes effect by itself.
- Portarr cannot make a client ask permission. The guards are the instruction to the assistant and the client's own prompts for file edits and commands. The page says so.
- `http://` needs `allowInsecure: true` on the server (D-103). A `stdio` command is a program and its arguments, never a shell line. An unpinned `npx` or `uvx` package is flagged on the server's page. A capture's text is capped, escaped when displayed and never treated as instructions.
- The health probe sends one fixed `initialize` request to the declared URL, without credentials, with the checks' timeout and body cap, and follows no redirect.

### Permissions (D-115)

A workstation's assistant connects with an API key of a user who holds `mcp.use` (Admin or Operator). A read-only key does not carry it (it keeps only the `*.view` permissions of its owner), so it sees none of the five MCP tools: create a full key for the account that will capture and install.

One new permission, **`mcp.use`**: see the MCP page, the definitions, the health, the plans and the inbox, call the MCP tools, and submit a capture. It is **not** a `.view` permission, so the Observer (every page, read only) and the Guest do not get it: someone who looks at the infrastructure is a consumer and has no use for the configuration of the operator's assistants. Admin and Operator hold it. Accepting a capture or editing a definition also needs `catalog.write`.

### The UI

- Sidebar entry **MCP**, route `/mcp-servers` (`/mcp` is Portarr's own endpoint), three tabs in the Maintenance tabs pattern.
- **Servers**: one row per server (icon, name, transport, state with "seen 2 min ago" or "down for 12 days", how many workstations want it). A detail page shows the definition, a block per client with the rendered operations and a copy button, and the wanted-on list.
- **Workstations**: a matrix, workstations in rows and servers in columns, a cell is *wanted* or empty. A click opens the workstation, its plan and the vault keys it needs.
- **Captured**: the inbox described above; empty until a capture arrives.
- The empty state explains the sections. All strings go through `translate()` (English source, D-26). Demo mode does not exist yet (C7): until the catalog declares servers the page shows its empty state.

### The API

All under `/api/v1/mcp`, all needing `mcp.use`:

| Endpoint | Returns |
|---|---|
| `GET servers`, `GET servers/{id}?os=` | The servers with their health; one server with its rendering for every client |
| `GET clients` | The supported clients, their scopes and formats |
| `GET workstations`, `GET workstations/{id}?os=` | The workstations; one plan (install, remove, variables, vault keys) |
| `GET captures` | The inbox, grouped by server, compared with the catalog |
| `POST captures/{key}/accept`, `POST captures/{key}/dismiss` | A person's decision: also needs `catalog.write` and a browser session (D-112); accept carries the `version` of the card that was read |

### Portarr's MCP tools

| Tool | Access | Returns |
|---|---|---|
| `list_mcp_servers` | `mcp.use` | Servers: transport, scope, state and last healthy, wanted on |
| `get_mcp_server` `server, client, scope?, os?, action?` (install or remove) | `mcp.use` | The rendering |
| `list_workstations` | `mcp.use` | Workstations, clients, wanted servers |
| `get_workstation_plan` `workstation, os?` | `mcp.use` | What to install, what to remove, the vault keys needed |
| `submit_mcp_capture` | `mcp.use`, writes the inbox only | The capture's id and what will be reviewed |

The server instructions (V5) gain: the catalog describes, you install; show the operator exactly what you write and wait; never send a secret value. The built-in chat (V6) uses these tools like any other client, with the permissions of its user; `submit_mcp_capture` is for an assistant running on a workstation.

## Slicing

One vertical, four steps, developed in the sandbox and delivered in one pull request (rule 7: one vertical, one PR).

1. **Servers and their health.** `mcpServers` section and validation, the `mcp` check type with the persisted last-healthy, the renderers for the main clients, `mcp.use`, the API, `list_mcp_servers` and `get_mcp_server`, the Servers tab.
2. **Workstations.** The `workstations` section, `list_workstations` and `get_workstation_plan`, the Workstations tab.
3. **Capture.** The inbox table, `submit_mcp_capture`, the Captured tab, identity and merge, the editor write by a person (D-112).
4. **Secrets.** The `secretStore` section, `secret:` references, the provisioners for the vault kinds, the vault keys in the plan.

## Acceptance criteria

- [x] A catalog with the sections loads; each validation error names the field; a literal secret is refused without echoing it
- [x] For each supported client, the rendering of an `http` and a `stdio` server matches its golden file and contains no secret value
- [x] A healthy fixture server, a 401 one, a dead one and a non-MCP one give the four states; the last healthy time survives a restart on SQLite and PostgreSQL; nothing probes a workstation
- [x] `mcp.use` is held by Admin and Operator and by neither Observer nor Guest; role × endpoint and role × tool matrices
- [x] The API, the MCP write tools and the chat cannot touch `mcpServers`, `workstations` or `secretStore`; the item endpoint leaves them out
- [ ] (the maintainer, on the preview) An assistant connected to the sandbox obtains the exact command to add a server at user scope for its client, and a workstation's plan with what to install and what to remove
- [x] A Down server appears in `get_overview` with the time since, for a caller with `mcp.use` only
- [x] (Step 3) A capture with a secret value is refused, field named; accepting a capture needs `catalog.write` and a browser session; the catalog audit shows the user
- [x] (Step 4) The plan lists the vault keys per workstation and the provisioning command per operating system; no secret value appears anywhere
- [x] Servers, Workstations and Captured tabs, empty states, strings through `translate()`; one Playwright journey; the Assistant page unchanged

## Definition of Done

[IMPLEMENTATION_PLAN.md §1.1](../IMPLEMENTATION_PLAN.md) applies to each of the four steps: unit, integration (SQLite and PostgreSQL), E2E, UI checklist, demo data (waits for C7), documentation (`docs/connectors/mcp-clients.md`: one section per client with the version checked), strings through `translate()`, no personal values, dogfooding on the maintainer's workstations in the preview, and the maintainer's acceptance.
