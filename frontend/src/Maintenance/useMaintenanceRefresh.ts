import { useQueryClient } from '@tanstack/react-query';

import useApiMutation from 'Helpers/Hooks/useApiMutation';
import type { MaintenanceRefresh } from 'typings/Maintenance';

/**
 * Asks Portarr to rescan GitHub and the registries now (at most once a minute; a read of external APIs, D-31).
 * The report is refetched at once: it then says `refreshing` until the new data is in.
 */
export default function useMaintenanceRefresh() {
  const queryClient = useQueryClient();
  const mutation = useApiMutation<undefined, MaintenanceRefresh>({ path: '/maintenance/refresh', method: 'POST' });

  return {
    ...mutation,
    mutate: () =>
      mutation.mutate(undefined, {
        onSettled: () => queryClient.invalidateQueries({ queryKey: ['api', '/maintenance'] }),
      }),
  };
}
