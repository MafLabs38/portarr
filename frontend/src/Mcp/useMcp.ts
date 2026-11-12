import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { McpServer, McpServerDetail, McpWorkstation, McpWorkstationPlan } from 'typings/Mcp';

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
