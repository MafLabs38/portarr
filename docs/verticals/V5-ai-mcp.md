# V5 — AI: MCP server and infrastructure knowledge

> Status: **in progress** (sandbox, phase 1). Pulls forward plan entry V16 AI ("Maintainer priorities (2026-10-30)"); the plan entry V5 Overview keeps its row. Features: F-API-03 (MCP server, read-only). Decisions: D-68 to D-71; catalog write tools and environment knowledge: D-83 to D-86 (accepted plan, step 5). Resolves Q-32.

## Need

> "An AI vertical: an MCP server or a knowledge base that an AI can query to know the infrastructure and what is happening on it. Really interesting to have this brick in the application."

An assistant (Claude Code, Claude Desktop, any MCP client) connected to Portarr answers from Portarr's live view instead of guessing:

| Question | Answered by |
|---|---|
| "What is down, and why?" | `get_overview`, `list_apps status=Down`, `get_app` |
| "Which machine is struggling, and what consumes it?" | `get_overview` (busiest machines), `get_infrastructure` |
| "Which databases are not backed up?" | `get_data_coverage onlyAtRisk=true` |
| "What is vulnerable or outdated?" | `get_maintenance` |
| "Where does app X run, and how do I reach it?" | `get_app`, `find_workload`, the knowledge base |
| "How do I reach node-2? Is it tunnelled to node-1? Which providers host my sites?" | `get_environment_knowledge`, the knowledge base (D-84) |
| "Note that the blog moved to the CDN" | the write tools, when enabled: `get_catalog_item`, `set_notes`, `upsert_app`… (D-86) |

The four tabs answer four questions; the assistant asks them all, mixes them and explains.

## Scope

| In | Out (later) |
|---|---|
| An MCP server inside Portarr (official C# SDK), Streamable HTTP at `/mcp`, stateless | stdio transport, a separate binary; prompts; subscriptions and change notifications |
| Seven read-only tools over the views the pages show (fleet, Infrastructure, Data, Maintenance) | Any tool that acts (start, stop, restart, open a PR): out of scope (D-31) |
| Two Markdown resources: the infrastructure knowledge base and the glossary, generated on each read | Issues, events and history (they come with V5 Overview / V6 Notifications); logs; long-term metrics (Prometheus, V8) |
| Off by default; keyed (per-user API keys since V7, D-97; the shared key is deprecated) | OAuth for MCP clients (D-99, step 2) |
| An **Assistant** page: server state, how to connect, knowledge preview | An assistant chat inside Portarr: [V6](V6-chat.md), through an optional agent runner; Portarr itself never calls an LLM |

## Design

### Server (D-68)

- Package `ModelContextProtocol.AspNetCore` **2.3.0**, in its own assembly `Portarr.Mcp` (tools, resources, knowledge base, authentication, the `/api/v1/assistant` controller), referenced by the host.
- `AddMcpServer().WithHttpTransport(Stateless = true)`: no session, no server-to-client requests; each POST is one JSON-RPC exchange (answered as JSON or one server-sent event).
- Server instructions tell the assistant to call `get_overview` first, to read `portarr://knowledge/infrastructure` for the map, and that nothing it calls changes the infrastructure.
- Tools are thin: a `FleetDigest` re-reads `IFleetService`, `IInfrastructureMonitor`, `IDataService` and `IMaintenanceService`, through the API mappers already used by the pages (`InfrastructureResourceBuilder`, extracted from the Infrastructure controller, `DataResourceMapper`, `MaintenanceResourceMapper`, `EnvironmentConnection`). Its only own work is picking, ordering and shortening.
- Outputs are typed records (structured content with an output schema, and the same JSON as text), null fields left out, CPU in cores, memory in GiB, percentages of the machine's own capacity. Lists are capped (10 subjects per finding, 50 items per list) and say so.

| Tool | Arguments | Returns |
|---|---|---|
| `get_overview` | — | App counts; **needs attention** (grouped findings, danger → warning → info: `app-down`, `environment-unreachable`, `machine-not-ready`, `backup-failed`, `data-missing`, `backup-late`, `backup-tool-not-set-up`, `critical-without-offsite`, `critical-offsite-pending`, the Maintenance attention kinds…); environments and their connection; the 5 busiest machines; data and maintenance counts; catalog errors (count only) |
| `list_apps` | `status`, `stack`, `environment` (optional) | Apps, worst first: status and reason, stack and role, environments, main URL |
| `get_app` | `app` (id or name) | Status and reason, URLs, workloads with environment, machine, hypervisor, image and usage, the HTTP check, data assets and coverage, images and verdicts, repository. Unknown app: an error naming close matches |
| `get_infrastructure` | `environment` (optional) | Machines: kind, readiness, roles, scope, CPU and memory now / lowest / highest over the live window, capacity, hypervisor, guests, top 5 consumers with their apps |
| `find_workload` | `query` (1–200 characters) | Containers and pods whose name, image, namespace or app contains it, worst first |
| `get_data_coverage` | `onlyAtRisk` | Followed data by state and "x of y critical assets have an off-site copy", assets at risk first (importance, accepted window, local and off-site copy with state, reason and jobs), the backup tools read |
| `get_maintenance` | `section`: all, code or images | Summary, attention list, repositories with something to say, images not up to date |

The live window is described as now, lowest and highest point: the chart in words, not a metric store (D-30).

### Knowledge base (D-69)

`portarr://knowledge/infrastructure` (`text/markdown`), built on each read:

1. **Summary**: counts and what needs attention.
2. **Environments and machines**: a tree, VMs under their hypervisor (an environment's `host`, a node's `nodeHosts` entry), each machine with its size, load now and top consumers; LAN addresses from the catalog.
3. **Stacks and apps**: one table per stack (app, status and reason, role, where it runs, URLs), then the apps outside a stack.
4. **Data and backups**: every followed asset with its importance and copies, then the backup tools read.
5. **Code and images**: repositories with findings, images behind.

`portarr://knowledge/glossary`: what statuses, roles, connections, coverage states, image verdicts and reason codes mean, and that Portarr never acts. A real fleet of 48 apps gives about 21 KB.

### Security model (D-70)

- **Off by default.** `PORTARR__MCP__ENABLED=true` maps `/mcp`. Since V7 (D-97) a call needs a per-user API key (Account, API keys), the chat runner's token, or the deprecated `PORTARR__MCP__APIKEY` (optional, at least 32 characters when set; acts as the `system` principal, Admin). Each tool needs the permission of the page it mirrors (D-98).
- **API key** in `X-Api-Key` or `Authorization: Bearer`; SHA-256 of both sides compared with `CryptographicOperations.FixedTimeEquals` (no content or length timing leak). Missing or wrong: **401** with `WWW-Authenticate: Bearer realm="portarr-mcp"`. The key is never returned (`/api/v1/assistant` only says whether one is set) and never logged.
- **Rate limit**: 120 requests per minute per client address (forwarded only from trusted proxies), counted before authentication so wrong keys are throttled too; then **429**.
- **No secrets in answers**: URLs lose credentials, query and fragment (`SafeUrl`); workload labels, environment variables, Docker and Kubernetes endpoints and token variable names are never part of a tool output; failures are reason codes; catalog errors are a count.
- **Read-only**: every tool is annotated `readOnlyHint`, `idempotentHint`, `!destructiveHint`, `!openWorldHint`, and only reads memory: a call reaches no connector.
- **Off means absent**: `/mcp` and `/.well-known/*` are plain 404 (POST: 405), never the UI, so an MCP client probing for OAuth metadata gets a clean answer.
- Exposure: the endpoint is behind the same sign-in model as the rest of Portarr: per-user keys, read-only or full, never beyond the owner's role (V7).

### Write tools and environment knowledge (D-83 to D-86)

- The read tool `get_environment_knowledge` (always on) returns what the operator wrote in the catalog: notes, access (SSH alias or command line, key file *name*, jump host, consoles), machines of an environment, network links and external providers, optionally narrowed to one environment. The knowledge base gains an "Access, network and providers" section and the notes of stacks and apps.
- `PORTARR__MCP__WRITETOOLS=true` adds the catalog write tools (`get_catalog_item`, `upsert_app`, `remove_app`, `upsert_stack`, `upsert_environment`, `upsert_machine`, `upsert_data_asset`, `add_link`, `upsert_provider`, `remove_catalog_item`, `set_notes`) and the identity tools `get_portal` and `set_portal` (name and logo shown in the sidebar, D-91; the logo is chosen among the files already in the `branding` folder). They need `PORTARR__CATALOG__WRITABLE=true` and share the API's editor: same versions (a stale one is refused with the current one in the message), validation, backups and audit (channel `mcp`). Off, they are not listed. See [catalog.md](../catalog.md#editing-through-the-api).
- The Assistant page marks the write tools and shows a "Catalog edits" card: whether edits are on, and the latest audited edits.

## Connecting

Claude Code (user scope, run by the operator):

```bash
claude mcp add --transport http portarr https://portarr.homelab.example/mcp --header "X-Api-Key: <your key>"
```

Claude Desktop (`claude_desktop_config.json`, through the `mcp-remote` bridge, Node.js required):

```json
{
  "mcpServers": {
    "portarr": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://portarr.homelab.example/mcp", "--header", "X-Api-Key:${PORTARR_API_KEY}"],
      "env": { "PORTARR_API_KEY": "<your key>" }
    }
  }
}
```

Any client by hand:

```bash
curl -s https://portarr.homelab.example/mcp -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_overview","arguments":{}}}'
```

## UI (D-71)

Sidebar entry **Assistant** after Maintenance (route `/assistant`). Its own entry because the sidebar has no second level yet and it is a way of asking the four tabs' questions; it moves under Settings or System with C3.

1. **MCP server** card: on/off, endpoint (`<origin>/mcp`), transport, authentication (header name, key set or not), access (read-only), the tools and resources with their descriptions.
2. **Connect an assistant** card: the Claude Code command and the Claude Desktop configuration with a copy button, the key as a placeholder; example questions.
3. **Knowledge base**: the same Markdown the assistant reads, rendered by a small built-in renderer (headings, lists, tables, code, bold, http(s) links; no HTML is interpreted), one collapsible `<details>` per section, refreshed every minute.

When the server is off, an info alert says how to turn it on.

## Acceptance criteria

- [x] `/mcp` is not served unless `PORTARR__MCP__ENABLED=true`; startup fails when it is on without a key of 32+ characters
- [x] Without the key, or with a wrong one: 401; with `X-Api-Key` or a bearer token: initialize, tools/list, tools/call and resources/read work
- [x] More than 120 requests per minute from one client: 429, wrong keys included
- [x] Seven tools, all read-only and closed-world, reusing the Core services and the API mappers; none acts
- [x] `get_overview` lists what needs attention across apps, infrastructure, data and maintenance, worst first, naming its subjects
- [x] `get_app` tells where an app runs down to the machine and the hypervisor, and how to reach it
- [x] The knowledge base nests VMs under their hypervisor and lists stacks, apps, URLs, data, jobs, repositories
- [x] No answer contains URL credentials or queries, labels, environment variables, connector endpoints or token names
- [x] The Assistant page shows the state, the tools, how to connect (key never shown) and the knowledge base
- [ ] E2E test of the Assistant page and a check with the MCP Inspector (phase 2, before the PR)

## First run on the maintainer's fleet (sandbox, 2026-10-30)

| | |
|---|---|
| `get_overview` | 48 apps (46 healthy, 1 down: HTTP 502, 1 unknown: healthcheck starting); 3 followed assets whose backup failed, 2 missing, 1 of 7 critical assets without an off-site copy; 7 repositories with CI failing, 6 images with a new major version, Dependabot off on 22 of 25 repositories; 5 environments connected; busiest machine a Kubernetes node at 18 % memory |
| Knowledge base | about 21 KB: a hypervisor with two VM environments and one Kubernetes node under it, 11 stacks |
| Security | 401 without or with a wrong key; 429 after 120 requests in a minute |
