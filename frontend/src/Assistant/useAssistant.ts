import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type AssistantStatus from 'typings/Assistant';
import type { CatalogAuditEntry, Knowledge } from 'typings/Assistant';

/** The MCP server's state: enabled, endpoint, tools and resources (V5). */
export function useAssistantStatus() {
  return useApiQuery<AssistantStatus>({ path: '/assistant' });
}

/** The knowledge base an assistant reads, generated from the live state on each request. */
export function useKnowledge(enabled: boolean) {
  return useApiQuery<Knowledge>({ path: '/assistant/knowledge', enabled, refetchInterval: 60_000 });
}

/** The latest catalog edits, newest first (D-85). */
export function useCatalogAudit(enabled: boolean) {
  return useApiQuery<CatalogAuditEntry[]>({ path: '/catalog/audit?limit=5', enabled, refetchInterval: 30_000 });
}
