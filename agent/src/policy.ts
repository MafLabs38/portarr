// What a tool call may do (D-88), decided by the runner whatever the CLI: Portarr's read tools run, Portarr's
// other tools (the catalog write tools) wait for the operator, anything else is refused without asking.

/** The MCP server's name in the CLI's configuration: its tools are `mcp__portarr__<tool>`. */
export const serverName = 'portarr';

export type ToolClass = 'read' | 'write' | 'forbidden';

const toolPattern = /^mcp__portarr__([a-z][a-z_]{0,63})$/;
const readPrefixes = ['get_', 'list_', 'find_'];

export function classify(toolName: string): ToolClass {
  const match = toolPattern.exec(toolName);
  if (!match) {
    return 'forbidden';
  }
  return readPrefixes.some((prefix) => match[1].startsWith(prefix)) ? 'read' : 'write';
}

/** `mcp__portarr__set_notes` → `set_notes`, for people. */
export function shortToolName(toolName: string): string {
  return toolPattern.exec(toolName)?.[1] ?? toolName;
}

/** Portarr's read tools at the time of writing, pre-allowed on the CLI's command line (others are classified). */
export const knownReadTools = [
  'get_overview',
  'list_apps',
  'get_app',
  'get_infrastructure',
  'find_workload',
  'get_data_coverage',
  'get_maintenance',
  'get_environment_knowledge',
  'get_catalog_item',
  'get_portal',
].map((tool) => `mcp__${serverName}__${tool}`);

export const rejectedMessage = 'The operator rejected this change. Do not retry it; ask what they want instead.';
export const forbiddenMessage = 'Only Portarr tools are available in this chat.';
export const cancelledMessage = 'The operator stopped this turn.';
