import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import {
  type CatalogEditOutcome,
  type CatalogItem,
  type CatalogSectionItems,
  type CatalogSectionName,
  catalogSections,
  type PendingChange,
} from 'typings/Catalog';

const pendingPath = '/catalog/pending';

export const sectionPath = (section: CatalogSectionName) => `/catalog/${section}`;

/** What an edit refreshes: the waiting changes and every section (an MCP server or a workstation included). */
const invalidateAll = [pendingPath, ...catalogSections.map(sectionPath)];

export function useCatalogSection(section: CatalogSectionName) {
  return useApiQuery<CatalogSectionItems>({ path: sectionPath(section) });
}

export function usePendingChanges() {
  return useApiQuery<PendingChange[]>({ path: pendingPath, refetchInterval: 15_000 });
}

interface ItemBody {
  section: CatalogSectionName;
  item: CatalogItem;
  version: string;
}

/** Creates or replaces an item: only what changed is rewritten in the file. A change of how a host is reached may answer `pending`. */
export function useSaveItem() {
  return useApiMutation<ItemBody, CatalogEditOutcome>({
    path: ({ section, item }) => `${sectionPath(section)}/${item.id}`,
    method: 'PUT',
    invalidate: invalidateAll,
    serialize: ({ item, version }) => JSON.stringify({ version, item }),
  });
}

/** Checks an item as a save would, writing nothing. */
export function useValidateItem() {
  return useApiMutation<ItemBody, CatalogEditOutcome>({
    path: ({ section, item }) => `${sectionPath(section)}/${item.id}/validate`,
    method: 'POST',
    invalidate: [],
    serialize: ({ item, version }) => JSON.stringify({ version, item }),
  });
}

export function useRemoveItem() {
  return useApiMutation<{ section: CatalogSectionName; id: string; version: string }, CatalogEditOutcome>({
    path: ({ section, id, version }) => `${sectionPath(section)}/${id}?version=${encodeURIComponent(version)}`,
    method: 'DELETE',
    invalidate: invalidateAll,
  });
}

export function useApprovePending() {
  return useApiMutation<{ id: string }, CatalogEditOutcome>({
    path: ({ id }) => `${pendingPath}/${id}/approve`,
    method: 'POST',
    invalidate: invalidateAll,
  });
}

export function useRejectPending() {
  return useApiMutation<{ id: string }, undefined>({
    path: ({ id }) => `${pendingPath}/${id}`,
    method: 'DELETE',
    invalidate: [pendingPath],
  });
}
