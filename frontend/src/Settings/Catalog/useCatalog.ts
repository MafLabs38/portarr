import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type {
  CatalogEditOutcome,
  CatalogItem,
  CatalogSectionItems,
  CatalogSectionName,
  PendingChange,
} from 'typings/Catalog';

const pendingPath = '/catalog/pending';

export const sectionPath = (section: CatalogSectionName) => `/catalog/${section}`;

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
    invalidate: [
      pendingPath,
      ...['apps', 'environments', 'stacks', 'data', 'links', 'providers'].map((s) => `/catalog/${s}`),
    ],
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
    invalidate: [
      pendingPath,
      ...['apps', 'environments', 'stacks', 'data', 'links', 'providers'].map((s) => `/catalog/${s}`),
    ],
  });
}

export function useApprovePending() {
  return useApiMutation<{ id: string }, CatalogEditOutcome>({
    path: ({ id }) => `${pendingPath}/${id}/approve`,
    method: 'POST',
    invalidate: [
      pendingPath,
      ...['apps', 'environments', 'stacks', 'data', 'links', 'providers'].map((s) => `/catalog/${s}`),
    ],
  });
}

export function useRejectPending() {
  return useApiMutation<{ id: string }, undefined>({
    path: ({ id }) => `${pendingPath}/${id}`,
    method: 'DELETE',
    invalidate: [pendingPath],
  });
}
