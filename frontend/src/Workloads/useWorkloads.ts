import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type Workload from 'typings/Workload';
import type { Environment, WorkloadUsage } from 'typings/Workload';

const refetchInterval = 15_000;

export function useWorkloads() {
  return useApiQuery<Workload[]>({ path: '/workloads', refetchInterval });
}

export function useEnvironments() {
  return useApiQuery<Environment[]>({ path: '/environments', refetchInterval });
}

function workloadPath(environmentId: string, name: string) {
  return `/workloads/${encodeURIComponent(environmentId)}/${encodeURIComponent(name)}`;
}

export function useWorkload(environmentId: string, name: string) {
  return useApiQuery<Workload>({ path: workloadPath(environmentId, name), refetchInterval, retry: false });
}

/** Polled every 5 s while the page is open, only once the workload is known to exist. */
export function useWorkloadUsage(environmentId: string, name: string, enabled: boolean) {
  return useApiQuery<WorkloadUsage>({
    path: `${workloadPath(environmentId, name)}/usage`,
    refetchInterval: 5_000,
    enabled,
    retry: false,
  });
}
