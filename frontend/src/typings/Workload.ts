import type Status from './Status';

export interface Environment {
  id: string;
  name: string;
  runtime: string;
  connection: 'connected' | 'unreachable' | 'pending' | 'not-connected';
  /** Reason code: unreachable, timeout, invalid-response, http-<code>. */
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
  truncated: boolean;
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
