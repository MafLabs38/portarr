# V6 — Built-in chat

> **Naming:** this page is called V6 because the chat was built sixth in the sandbox order (step 6 of the plan's list of done work). The plan's own **V6** is *Notifications and heartbeats*, a different vertical; in other documents the chat is "the built-in chat".

> Status: **in progress** (sandbox). Step 6 of the accepted plan ([IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md)). Decisions: D-87 to D-90, [ADR 0008](../adr/0008-headless-agent-cli-behind-an-adapter.md). Builds on the MCP server and its write tools (V5, D-68 to D-71, D-83 to D-86).

## Need

> "A chat panel in Portarr, like the Claude Code panel in VS Code: click an icon, a side panel opens with the conversation history, a prompt box where I can paste text (my voice dictation tool pastes), agent and model selection, tool calls shown. Behind it an agent CLI in a separate optional container. Its only tool is Portarr's own MCP server: it can describe and change Portarr's configuration with my confirmation; it has no shell and no access to the machines. E.g. 'add this VM to the environment', 'note that X uses Vercel'."

## Scope

| In | Out (later) |
|---|---|
| An optional `portarr-agent` container ([`agent/`](../../agent/)) running Claude Code headless, one process per active conversation | Other CLIs (Codex, Gemini CLI…): one adapter each, same interface |
| Portarr's MCP server as the only tool; reads run, writes wait for the operator | Any other tool: shell, files, web, other MCP servers |
| A proxy in Portarr (`/api/v1/assistant/chat`), off without `PORTARR__AGENT__URL` | Per-user conversations and roles: done in V7 (D-98) |
| A side panel: history, Markdown answers, collapsible tool calls, permission cards, paste-friendly prompt, agent and model, Stop, usage | Attachments, images, voice in the browser |

## Design

### The runner (D-87)

```
browser ── /api/v1/assistant/chat (Portarr, proxy, D-89) ── bearer secret ──▶ portarr-agent :8488
                                                                               │ one `claude -p` per active conversation
                                                                               │ stdin/stdout: stream-json + control protocol
                                                                               ▼
                                                    Portarr /mcp (X-Api-Key, X-Portarr-Origin: chat:<id>)
```

- **Node.js 22, TypeScript run directly** (type stripping), no runtime dependency: the CLI it drives is a Node program, so the container holds one runtime, and the runner's code is small enough (HTTP API, sessions, a line parser) not to need a framework. Tests with `node --test` on recorded, sanitized stream-json fixtures (`agent/test/fixtures`).
- **Headless, structured, never a PTY**: `claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages`. Messages go in as JSON lines; out come `system/init` (the session id), `stream_event` text deltas, `assistant` blocks (text, thinking, tool use), `user` tool results and `result` (usage and cost). See [ADR 0008](../adr/0008-headless-agent-cli-behind-an-adapter.md).
- **Adapter interface** (`agent/src/adapters/adapter.ts`): `start({ model, resume, sessionId, workDir, mcpConfigFile, onEvent, onExit })` returns a process with `send`, `answer(requestId, decision)`, `interrupt`, `close`, `kill`; the adapter turns its CLI's output into the runner's events (`user`, `text-delta`, `text`, `thinking`, `tool-call`, `tool-result`, `permission`, `permission-resolved`, `turn-end` with usage, `status`, `error`). A Codex adapter would run `codex exec --json` (or its app-server protocol) with Portarr as its only MCP server, map its JSON events to the same ones and its approval requests to `permission-request`; the session manager, the policy, the HTTP API and the panel stay unchanged.
- **Sessions**: one process per active conversation, started on its first message (`--session-id`), resumed later (`--resume`); stopped after `PORTARR_AGENT_IDLE_MINUTES` idle; at most `PORTARR_AGENT_MAX_SESSIONS`, the least recently used idle one making room (busy ones: 429). Changing the model restarts the process on the same session. Cancel interrupts the turn (control request `interrupt`), refuses pending writes, kills after 5 s.
- **History**: one JSON file per conversation (`conversations/<id>.json`, atomic write, latest 2000 events); text deltas and status are live only. The event stream replays the stored events after `after` (or `Last-Event-ID`) then streams live ones, so a reopened panel or a reconnect misses nothing.

### What the agent may do (D-88)

| Command line | Why |
|---|---|
| `--strict-mcp-config --mcp-config <file>` | Portarr's `/mcp` only, from a per-process file (mode 600, removed when the process ends) carrying the MCP key and `X-Portarr-Origin: chat:<conversation>` |
| `--tools ""` and `--disallowedTools Bash,Edit,Write,Read,Glob,Grep,WebFetch,WebSearch,Task…` | No built-in tool at all |
| `--allowedTools mcp__portarr__get_…` | Portarr's read tools run without asking |
| `--permission-prompt-tool stdio` | Every other call is asked over the control protocol (`control_request` `can_use_tool` → `control_response` `allow` / `deny`); the runner decides |
| `--setting-sources ""`, empty working directory, own `CLAUDE_CONFIG_DIR` and `HOME` | No settings, hooks or `CLAUDE.md` (verified: a `CLAUDE.md` in a parent directory is ignored) |
| `--system-prompt` | A short prompt for this job (read first, change only what was asked, never write a secret), instead of a coding assistant's |
| never `--dangerously-skip-permissions` | |

The runner's policy (`agent/src/policy.ts`): `mcp__portarr__get_*`, `list_*`, `find_*` are allowed; any other `mcp__portarr__*` tool becomes a **permission card** and waits for the operator; anything else is denied without asking. Approve sends the tool's input unchanged; Reject answers "The operator rejected this change. Do not retry it; ask what they want instead."

### Portarr's side (D-89)

- `PORTARR__AGENT__URL` and `PORTARR__AGENT__SECRET` (32+ characters, startup fails otherwise). Without a URL every chat endpoint answers 404 and `GET /api/v1/assistant/chat` says `enabled: false`: the icon is hidden.
- Endpoints under `/api/v1/assistant/chat`: `GET` (state, agents and models), `GET|POST conversations`, `GET|DELETE conversations/{id}`, `POST conversations/{id}/messages`, `GET conversations/{id}/events` (SSE, counted with the log streams), `POST conversations/{id}/permissions/{requestId}` (`{ approve }`), `POST conversations/{id}/cancel`. Ids and names are checked before anything leaves Portarr; nothing of the browser's request is forwarded; a runner that refuses the secret or does not answer is a 502.
- Writes go through the MCP write tools, so through the catalog editor: versions, validation, backups and the audit (channel `mcp`). The runner sends `X-Portarr-Origin: chat:<conversation>`, kept in the audit entry as `via`; the Assistant page shows such edits "through mcp (built-in chat)".

### The panel (D-90)

- A chat icon at the right of the header (only when the chat is on) opens a side panel to the right of the page, resizable from its left edge (pointer or arrow keys, 320 to 960 px, kept per browser); full screen under 768 px.
- Header: the conversation's title, **Conversations** (past ones, latest first, with model and age or "waiting for your approval", delete), **New chat**, close.
- Messages (`role="log"`): the operator's messages as bubbles; answers rendered by the safe built-in Markdown renderer (no HTML interpreted); thinking collapsed; each tool call collapsed with its state (running, done, failed), open for its arguments and a shortened result; permission cards "Apply this change?" with the tool and its arguments and **Approve** / **Reject**, then their outcome; a usage line per turn (tokens, cost as the CLI reports it, duration); errors as alerts. The answer being written shows live.
- Prompt: a textarea that accepts typing and pasting, several lines; Enter sends, Shift+Enter breaks the line (IME composition respected); agent and model selectors (`sonnet`, `opus`, `haiku` → `--model`); **Stop** while a turn runs.

## Security limits

- Per user since V7 (D-98): the chat needs `chat.use`, a conversation belongs to its creator and approving a change needs `catalog.write`. (Before accounts, whoever could open Portarr could use the chat and approve its catalog edits, like the rest of the UI: keep Portarr on a trusted network or behind the reverse proxy's SSO. The chat writes only through the user's own MCP token, and only the catalog (D-31).)
- `X-Portarr-Origin` is informative: any client holding a key could send it. With a chat token (D-98) the audit's `via` comes from the token, which is authoritative.
- The agent sees what Portarr's tools return (the fleet as the pages show it); tool results kept in the history are shortened to 1500 characters. The model provider receives the conversation.
- The approval card shows the arguments the CLI will send; approving sends them unchanged (the runner never edits them).

## Acceptance criteria

- [x] No chat icon and 404 endpoints without `PORTARR__AGENT__URL`
- [x] The runner refuses requests without its secret; the CLI's environment holds neither the secret nor the MCP key
- [x] A question is answered with Portarr's read tools, shown as tool calls
- [x] A write shows "Apply this change?"; Reject leaves the catalog untouched; Approve writes it, audited `mcp` via `chat:<id>`
- [x] Conversations are listed and resumed (the CLI session continues, after an idle stop or a restart)
- [x] Paste, multi-line, Enter / Shift+Enter, model choice, Stop
- [ ] E2E journey with a fake runner (phase 2, before the PR)

## First run (sandbox, 2026-10-31, Claude Code 2.1.63, Haiku)

| Check | Result |
|---|---|
| "Which machines are in my infrastructure and how is node-2 connected to node-1?" | `get_infrastructure` and `get_environment_knowledge`, then a three-sentence answer naming the WireGuard tunnel; 6.4 s |
| "Add a note to the provider …" | `get_catalog_item`, then `set_notes` → a permission card; rejected after 40 s: the provider untouched, no audit entry; asked again, approved: applied, audit `mcp`, `via: chat:<id>` |
| A runner restart while a write waited | the card turned "Cancelled", the next message resumed the CLI session (it remembered its proposal) |
| Resume | an idle conversation reopened: "what was my previous question?" answered from the resumed session |
