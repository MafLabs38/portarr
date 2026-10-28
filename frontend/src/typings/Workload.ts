import type Status from './Status';

export interface Environment {
  id: string;
  name: string;
  runtime: string;
  reachable: boolean;
  error: string | null;
  workloads: number;
  refreshedAt: string | null;
}

export interface WorkloadUsage {
  cpuCores: number;
  memoryBytes: number;
  memoryLimitBytes: number | null;
}

export interface LogLine {
  timestamp: string | null;
  stream: 'stdout' | 'stderr';
  text: string;
}

export default interface Workload {
  environmentId: string;
  id: string;
  name: string;
  image: string;
  state: string;
  health: string;
  statusText: string;
  createdAt: string;
  composeProject: string | null;
  appIds: string[];
  status: Status;
  stale: boolean;
}
