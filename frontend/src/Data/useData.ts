import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type DataReport from 'typings/Data';

// Built from the inventory (15 s) and the backup reads (1 min): a 30 s refresh is enough.
const refetchInterval = 30_000;

/** Data assets, their coverage and the known backup jobs (D-60 to D-62). */
export default function useData() {
  return useApiQuery<DataReport>({ path: '/data', refetchInterval });
}
