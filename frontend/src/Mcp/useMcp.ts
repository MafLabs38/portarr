import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type {
  McpAcceptRequest,
  McpAcceptResult,
  McpCaptureGroup,
  McpServer,
  McpServerDetail,
  McpWorkstation,
  McpWorkstationPlan,
} from 'typings/Mcp';

// A remote server is checked every few minutes: a page open for a while follows without asking every second.
const refetchInterval = 30_000;

/** The operator's MCP servers with their health. */
export function useMcpServers() {
  return useApiQuery<McpServer[]>({ path: '/mcp/servers', refetchInterval });
}

/** One server and how to install it in every client, for an operating system (windows, linux or macos). */
export function useMcpServer(id: string, os: string) {
  return useApiQuery<McpServerDetail>({
    path: `/mcp/servers/${encodeURIComponent(id)}?os=${encodeURIComponent(os)}`,
    refetchInterval,
  });
}

/** The machines the operator works from with an assistant, and the servers wanted on each. */
export function useMcpWorkstations() {
  return useApiQuery<McpWorkstation[]>({ path: '/mcp/workstations', refetchInterval });
}

/** The plan of one workstation, for its own system unless another is given. */
export function useMcpWorkstation(id: string, os?: string) {
  const query = os ? `?os=${encodeURIComponent(os)}` : '';
  return useApiQuery<McpWorkstationPlan>({
    path: `/mcp/workstations/${encodeURIComponent(id)}${query}`,
    refetchInterval,
  });
}

/** What the assistants handed over from the workstations and nobody decided yet, compared with the catalog (D-116). */
export function useMcpCaptures() {
  return useApiQuery<McpCaptureGroup[]>({ path: '/mcp/captures', refetchInterval });
}

// A decision changes the inbox, and may change the servers and the workstations.
const afterDecision = ['/mcp/captures', '/mcp/servers', '/mcp/workstations'];

/** A person accepts a group: the definition and the wanted lists are written to the catalog (a browser session with catalog.write). */
export function useAcceptCapture() {
  return useApiMutation<McpAcceptRequest, McpAcceptResult>({
    path: (body) => `/mcp/captures/${encodeURIComponent(body.key)}/accept`,
    method: 'POST',
    invalidate: afterDecision,
    serialize: (body) =>
      JSON.stringify({
        mode: body.mode,
        id: body.id,
        name: body.name,
        variables: body.variables,
        addToWorkstations: body.addToWorkstations,
      }),
  });
}

/** A person dismisses a group: it leaves the inbox, nothing is written. */
export function useDismissCapture() {
  return useApiMutation<{ key: string }, { dismissed: number }>({
    path: (body) => `/mcp/captures/${encodeURIComponent(body.key)}/dismiss`,
    method: 'POST',
    invalidate: ['/mcp/captures'],
    serialize: () => '{}',
  });
}
