import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type App from 'typings/App';
import type CatalogStatus from 'typings/CatalogStatus';
import type Stack from 'typings/Stack';

// The inventory refreshes every 15 s on the server: polling at the same pace keeps statuses current.
const refetchInterval = 15_000;

export function useApps() {
  return useApiQuery<App[]>({ path: '/apps', refetchInterval });
}

export function useStacks() {
  return useApiQuery<Stack[]>({ path: '/stacks', refetchInterval });
}

export function useCatalogStatus() {
  return useApiQuery<CatalogStatus>({ path: '/catalog/status', refetchInterval });
}
