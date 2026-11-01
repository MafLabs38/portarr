/** A point of a machine's live chart (oldest first, D-54). */
export interface MachinePoint {
  at: string;
  cpuCores: number | null;
  memoryBytes: number | null;
  /** On a hypervisor: the load its VMs report from the inside at that moment (D-59), not host-measured. */
  guestCpuCores: number | null;
}

/** A point of a machine's host chart, one per second (D-72). */
export interface HostPoint {
  at: string;
  /** 100 − idle, I/O wait included, in % of the machine. */
  cpuPercent: number | null;
  iowaitPercent: number | null;
  memoryUsedBytes: number;
  swapUsedBytes: number;
}

/**
 * What a machine's host agent (node_exporter) reads every second (D-72): the machine as its kernel sees it.
 * Memory: used = total − available, reclaimable = available − free; used + reclaimable + free = total.
 */
export interface HostMetrics {
  environmentId: string;
  machineId: string;
  source: 'node-exporter';
  sampledAt: string | null;
  /** Reason code of the last read when it failed; the last values are kept. */
  error: string | null;
  intervalSeconds: number;
  cores: number | null;
  cpuPercent: number | null;
  iowaitPercent: number | null;
  /** Load of each logical core, cpu 0 first. */
  corePercents: number[];
  load1: number | null;
  temperatureCelsius: number | null;
  memoryTotalBytes: number | null;
  memoryUsedBytes: number | null;
  memoryReclaimableBytes: number | null;
  memoryFreeBytes: number | null;
  swapTotalBytes: number | null;
  swapUsedBytes: number | null;
  /** Oldest first; empty on the live stream. */
  history: HostPoint[];
}

/** The hypervisor a machine is a VM on (D-59). */
export interface MachineHost {
  environmentId: string;
  name: string;
}

/**
 * A VM on a hypervisor, seen from the inside (D-59): cores and memory allocated to it as the guest reports
 * them, and its load. Scope containers: only the sum of its containers, a lower bound of its real load.
 */
export interface Guest {
  environmentId: string;
  machineId: string;
  name: string;
  cpuCapacityCores: number | null;
  memoryCapacityBytes: number | null;
  cpuCores: number | null;
  scope: 'containers' | 'host' | null;
}

/** A workload placed on a machine; usage is null when stopped or not sampled yet. */
export interface MachineWorkload {
  id: string;
  cpuCores: number | null;
  memoryBytes: number | null;
}

export interface Machine {
  id: string;
  name: string;
  kind: 'docker-host' | 'kubernetes-node';
  roles: string[];
  ready: boolean | null;
  cpuCapacityCores: number | null;
  memoryCapacityBytes: number | null;
  cpuCores: number | null;
  memoryBytes: number | null;
  /** What the usage measures: the sum of its containers (Docker alone) or the whole host. */
  scope: 'containers' | 'host';
  history: MachinePoint[];
  /** Sorted by consumption, stopped ones last. */
  workloads: MachineWorkload[];
  /** The hypervisor it is a VM on, from the catalog. */
  host: MachineHost | null;
  /** The VMs it hosts, from the catalog (empty for a machine that hosts none). */
  guests: Guest[];
  /** Its host agent, when the catalog declares one (D-72): wins over the container sums. */
  hostMetrics?: HostMetrics | null;
}

export interface InfrastructureEnvironment {
  id: string;
  name: string;
  runtime: string;
  sampledAt: string | null;
  /** Reason code (not connected, unreachable, sampling failed); the last machines are kept. */
  error: string | null;
  /** How often the server reads the environment (0 until its first sample). */
  intervalSeconds: number;
  /** How often its values really change (metrics-server averages over ~15–20 s); null: every reading is new. */
  resolutionSeconds: number | null;
  /** The live window kept in memory. */
  windowSeconds: number;
  machines: Machine[];
  /** Workload ids on no known machine (pending pods). */
  unplaced: string[];
}
