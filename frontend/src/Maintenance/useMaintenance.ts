import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type MaintenanceReport from 'typings/Maintenance';

// Repositories are scanned every 30 min and registries every 6 h: a minute is plenty. While a manual refresh
// runs, the page asks every two seconds so the new data shows as soon as it is there.
const refetchInterval = 60_000;
const refreshingInterval = 2_000;

/** The operator's repositories and the running images against their registries (D-64 to D-67). */
export default function useMaintenance() {
  return useApiQuery<MaintenanceReport>({
    path: '/maintenance',
    refetchInterval: (query) => (query.state.data?.refresh.refreshing ? refreshingInterval : refetchInterval),
  });
}
