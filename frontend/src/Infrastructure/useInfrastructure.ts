import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { InfrastructureEnvironment } from 'typings/Infrastructure';

/** Docker hosts are sampled every 3 s by the server: refetching every 2 s keeps the page live (D-54). */
export const refetchInterval = 2_000;

/** While host reads stream in every second (D-72), the snapshot only refreshes consumers and VMs. */
export const streamedRefetchInterval = 5_000;

/** Machines and their live window, from the server's memory (never stored, D-54). */
export default function useInfrastructure(streaming = false) {
  return useApiQuery<InfrastructureEnvironment[]>({
    path: '/infrastructure',
    refetchInterval: streaming ? streamedRefetchInterval : refetchInterval,
  });
}
