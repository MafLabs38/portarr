# MCP clients

Portarr renders an MCP server of the [catalog](../catalog.md#mcp-servers) for each of these AI assistants ([mcp-servers.md](../verticals/mcp-servers.md), D-113). A renderer (an adapter in `Portarr.Providers.McpClients`; the Core composes the generic parts) says, for a **scope** (`user`, `project`, `local`) and an **operating system**, the command to run and/or the entry to merge into the client's configuration file, and the variables the machine must provide. It never writes a secret value. Each renderer has a golden file under `src/Portarr.Providers.McpClients.Test/Golden/`: when a client changes its format, the diff of that file is the review.

The formats below were read from each client's own documentation on **2026-11-12**. The client formats move fast: when something here is wrong for the version you run, the golden file and the renderer are the one place to change.

| Client | Id | Scopes | File | Root key | Reads a variable as |
|---|---|---|---|---|---|
| [Claude Code](https://code.claude.com/docs/en/mcp) | `claude-code` | user, project, local | `~/.claude.json` (user, and local under the project's path); `.mcp.json` (project) | `mcpServers` | `${VAR}` |
| [Claude Desktop](https://modelcontextprotocol.io/docs/develop/connect-local-servers) | `claude-desktop` | user | `claude_desktop_config.json` in the application's folder | `mcpServers` | not documented |
| [Cursor](https://cursor.com/docs/context/mcp) | `cursor` | user, project | `~/.cursor/mcp.json`; `.cursor/mcp.json` | `mcpServers` | `${env:VAR}` |
| [VS Code with GitHub Copilot](https://code.visualstudio.com/docs/agents/reference/mcp-configuration) | `vscode` | user, project | the user profile's `mcp.json`; `.vscode/mcp.json` | `servers` | `${env:VAR}` |
| [Windsurf](https://docs.devin.ai/windsurf/plugins/cascade/mcp) | `windsurf` | user, project | `~/.config/devin/mcp_config.json` (`%AppData%\devin\` on Windows); `.devin/mcp_config.json` | `mcpServers` | `${env:VAR}` |
| [Gemini CLI](https://github.com/google-gemini/gemini-cli) | `gemini-cli` | user, project | `~/.gemini/settings.json`; `.gemini/settings.json` | `mcpServers` | `${VAR}` |
| [Codex CLI](https://developers.openai.com/codex/mcp) | `codex-cli` | user, project | `~/.codex/config.toml`; `.codex/config.toml` | `[mcp_servers.<name>]` (TOML) | no syntax: `env_http_headers`, `env_vars` |
| [OpenCode](https://opencode.ai/docs/mcp-servers/) | `opencode` | user, project | `~/.config/opencode/opencode.json`; `opencode.json` | `mcp.servers` | `{env:VAR}` |
| [Zed](https://zed.dev/docs/ai/mcp) | `zed` | user | the user's `settings.json` | `context_servers` | not documented |

## What each renderer writes

- **Remote server.** The address field is `url` for Claude Code, Cursor, VS Code, OpenCode and Zed, `serverUrl` for Windsurf and `httpUrl` for Gemini CLI (its `url` means server-sent events). Claude Code and VS Code write `"type": "http"`, OpenCode `"type": "remote"`. Codex CLI writes `url`, its literal headers in `http_headers` and the headers that come from a variable in `env_http_headers` (header name to variable name).
- **Local server.** `command`, `args` and `env` everywhere, except OpenCode (`type: local`, the command and its arguments in one `command` array, `environment`) and Codex CLI (`command`, `args`, the variables in `env_vars`, literal values in `[mcp_servers.<name>.env]`). VS Code adds `"type": "stdio"`.
- **A command, when the client has one**: `claude mcp add` (with the variables between single quotes, so the shell does not expand them into the file), `gemini mcp add` (remote servers), `code --add-mcp` (user profile) and `opencode mcp add`. The others are a file entry only.
- **Removal** ("if present"): `claude mcp remove` and `gemini mcp remove`, else the deletion of the entry from the file, shown to the operator and approved before anything is touched.

## What could not be verified

Several details were not on the pages read; each one is said in the notes of the rendering, and none is guessed silently.

- **Windows locations** of the user files of Claude Code, Cursor, VS Code, Codex CLI, OpenCode and Zed follow the usual conventions (`%USERPROFILE%`, `%APPDATA%`); only Claude Desktop and Windsurf document a Windows path.
- **Claude Desktop** documents no remote-server entry for its file and no variable syntax: a remote server is rendered through the `mcp-remote` bridge, the variables become placeholders the operator replaces, and the page says Claude Desktop can also add a remote server as a custom connector.
- **Zed** documents no variable syntax: placeholders, as for Claude Desktop.
- **Windsurf** documentation moved to Devin's; older builds read `~/.codeium/windsurf/mcp_config.json` (said in the notes).
- **OpenCode** documents two shapes: the current `mcp.servers.<name>` is rendered, and the notes say older versions put the entry directly under `mcp`.
- **`cmd /c npx` on native Windows** is advised in the notes when a local server runs through `npx`; whether each client needs it was not verified.

A client that cannot hold a server at the scope asked is rendered as **unsupported**, with the scopes it has, never as an empty success.
