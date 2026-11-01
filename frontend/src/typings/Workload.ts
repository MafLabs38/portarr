import type Status from './Status';

export interface Environment {
  id: string;
  name: string;
  runtime: string;
  connection: 'connected' | 'unreachable' | 'pending' | 'not-connected';
  /** Reason code: unreachable, timeout, invalid-response, http-<code>; when not connected: token-missing, account-invalid, ca-missing, ca-invalid. */
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
  /** Kubernetes pods: the namespace and the node they run on. */
  namespace: string | null;
  node: string | null;
  appIds: string[];
  status: Status;
  stale: boolean;
  /** Whether its connector serves live usage (Docker, Kubernetes) and logs (Docker). */
  supportsUsage: boolean;
  supportsLogs: boolean;
}
