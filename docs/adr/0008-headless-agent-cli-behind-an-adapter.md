# 0008 — A headless agent CLI behind an adapter, not a terminal

- **Status**: accepted
- **Date**: 2026-10-31
- **Spec**: D-87, D-88

## Context

The maintainer wants a chat panel in Portarr like the one an IDE gives to Claude Code: history, paste, model choice, tool calls, and confirmation before a change. The agent must be a real agent CLI (Claude Code first, others later) and its only tool must be Portarr's MCP server. Three ways to drive a CLI from a web page:

1. Run it in a pseudo-terminal and stream the screen (xterm.js), or scrape the screen to rebuild messages.
2. Call a model API directly from Portarr with Portarr's tools.
3. Run the CLI in its headless, structured mode and translate its events.

A terminal (1) gives no structure: tool calls, permission prompts and usage would be parsed from text drawn for humans, which breaks with every release, and a terminal in the browser is a shell-shaped surface the maintainer explicitly does not want. Calling an API (2) means Portarr re-implements an agent loop and calls an LLM itself (V5 kept that out), and it is tied to one provider.

## Decision

Option 3, in a separate optional container (`portarr-agent`):

- Claude Code runs as `claude -p --input-format stream-json --output-format stream-json`: one long-lived process per active conversation, messages in as JSON lines, typed events out. Permission prompts use the same pipes (`--permission-prompt-tool stdio`: a `control_request` of subtype `can_use_tool`, answered with a `control_response`), the protocol of the Agent SDK. Sessions continue with `--resume`.
- An **adapter** per CLI translates its protocol into the runner's events and permission requests. The session manager, the tool policy, the HTTP API, the history and the panel do not know which CLI runs.
- The runner, not the CLI, decides what a tool may do (reads run, Portarr writes ask the operator, everything else is refused), on top of the CLI's own restrictions (no built-in tools, only Portarr's MCP server, no settings).

## Consequences

- The panel gets exact tool calls, arguments, results, permission requests and usage, and stays the same when an adapter is added.
- The runner depends on the CLI's structured protocol, a documented integration surface (the Agent SDK uses it) that changes far less than its screen; the fixtures in `agent/test/fixtures` pin what the parser expects and the CLI version is pinned in the image.
- Portarr's image stays free of agent CLIs and model credentials; without the container the chat simply does not exist.
- Each active conversation costs one CLI process (about 100 to 200 MB): the runner caps them and stops idle ones.
