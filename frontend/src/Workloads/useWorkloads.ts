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

export function useWorkloadUsage(environmentId: string, name: string) {
  return useApiQuery<WorkloadUsage>({
    path: `/workloads/${encodeURIComponent(environmentId)}/${encodeURIComponent(name)}/usage`,
    refetchInterval: 5_000,
  });
}
