import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import type Workload from 'typings/Workload';

/** A running workload with what it consumes on its machine. */
export interface Consumer {
  workload: Workload;
  cpuCores: number;
  memoryBytes: number;
}

/** A machine with the environment it belongs to, as the dashboard lays them out. */
export interface PlacedMachine {
  environment: InfrastructureEnvironment;
  machine: Machine;
}

/** Share of a capacity (0 to 1), or null when either side is unknown. */
export function share(used: number | null, capacity: number | null): number | null {
  return used === null || capacity === null || capacity <= 0 ? null : used / capacity;
}

export function percent(fraction: number): string {
  return String(Math.round(fraction * 100));
}

/**
 * The running workloads of a machine, heaviest CPU first (then memory). Stopped ones consume nothing and are
 * not shown on this dashboard; neither are workloads without a sample yet.
 */
export function consumers(machine: Machine, workloads: Map<string, Workload>): Consumer[] {
  return machine.workloads
    .map((usage) => ({ usage, workload: workloads.get(usage.id) }))
    .filter(
      (entry): entry is { usage: Machine['workloads'][number]; workload: Workload } =>
        entry.workload?.state === 'running' && (entry.usage.cpuCores !== null || entry.usage.memoryBytes !== null),
    )
    .map(({ usage, workload }) => ({ workload, cpuCores: usage.cpuCores ?? 0, memoryBytes: usage.memoryBytes ?? 0 }))
    .sort((a, b) => b.cpuCores - a.cpuCores || b.memoryBytes - a.memoryBytes);
}

/**
 * What the VMs of a hypervisor take (D-59), as the guests report it from the inside: their load (a lower
 * bound when a guest only sees its containers, or is not sampled) and the memory allocated to them.
 */
export interface GuestTotals {
  cpuCores: number | null;
  cpuLowerBound: boolean;
  memoryBytes: number | null;
}

export function guestTotals(machine: Machine): GuestTotals | null {
  if (machine.guests.length === 0) {
    return null;
  }

  const cpu = machine.guests.filter((g) => g.cpuCores !== null).map((g) => g.cpuCores ?? 0);
  const memory = machine.guests.filter((g) => g.memoryCapacityBytes !== null).map((g) => g.memoryCapacityBytes ?? 0);
  return {
    cpuCores: cpu.length > 0 ? cpu.reduce((a, b) => a + b, 0) : null,
    cpuLowerBound: machine.guests.some((g) => g.scope !== 'host' || g.cpuCores === null),
    memoryBytes: memory.length > 0 ? memory.reduce((a, b) => a + b, 0) : null,
  };
}

/**
 * Every machine of every environment, a hypervisor followed by the VMs it hosts (D-59), otherwise in catalog
 * order, so the cards read like the hierarchy.
 */
export function placeMachines(environments: InfrastructureEnvironment[]): PlacedMachine[] {
  const placed = environments.flatMap((environment) =>
    environment.machines.map((machine) => ({ environment, machine })),
  );
  const order = new Map(environments.map((environment, index): [string, number] => [environment.id, index]));
  const root = (entry: PlacedMachine) => order.get(entry.machine.host?.environmentId ?? entry.environment.id) ?? 0;

  return placed
    .map((entry, index) => ({ entry, index }))
    .sort(
      (a, b) =>
        root(a.entry) - root(b.entry) ||
        Number(a.entry.machine.host !== null) - Number(b.entry.machine.host !== null) ||
        a.index - b.index,
    )
    .map(({ entry }) => entry);
}

/** The anchor of a machine's card, for links from its hypervisor's VM list. */
export function machineAnchor(environmentId: string, machineId: string): string {
  return `machine-${environmentId}-${machineId}`;
}

/**
 * A machine's host agent (D-72): the snapshot, then the reads streamed since, appended as points (the
 * snapshot's history already holds the older ones). The newest read gives the current values.
 */
export function mergeHost(
  snapshot: HostMetrics | null | undefined,
  reads: HostMetrics[] | undefined,
  windowSeconds: number,
): HostMetrics | null {
  const streamed = reads ?? [];
  const last = streamed.at(-1);
  const base = snapshot ?? (last ? { ...last, history: [] } : null);
  if (!base) {
    return null;
  }

  const history = [...base.history];
  let lastAt = history.at(-1)?.at ?? '';
  for (const read of streamed) {
    if (read.sampledAt !== null && read.error === null && read.sampledAt > lastAt) {
      history.push({
        at: read.sampledAt,
        cpuPercent: read.cpuPercent,
        iowaitPercent: read.iowaitPercent,
        memoryUsedBytes: read.memoryUsedBytes ?? 0,
        swapUsedBytes: read.swapUsedBytes ?? 0,
      });
      lastAt = read.sampledAt;
    }
  }

  const end = Date.parse(lastAt);
  const kept = Number.isNaN(end)
    ? history
    : history.filter((point) => Date.parse(point.at) >= end - windowSeconds * 1_000);
  const current = last && (last.sampledAt ?? '') >= (base.sampledAt ?? '') ? last : base;
  return { ...current, history: kept };
}

/** A part of a host's memory ring; the parts sum to the machine's total. */
export interface HostMemoryPart {
  key: 'system' | 'guests' | 'guestsSystem' | 'consumers' | 'reclaimable' | 'free';
  bytes: number;
}

/**
 * The host's memory as Unraid's legend shows it (D-72), parts that sum to the total. <b>Used</b> (total −
 * available) holds first the containers or pods (their measured usage), then, on a hypervisor, the VMs'
 * allocation (as the guests report it, D-59) and the rest, the host's own apps and kernel. A VM only takes
 * the host memory it has touched: when the allocations exceed what is left of used, the two cannot be told
 * apart and are one part, "VMs and system". Then the <b>reclaimable cache</b> (available − free) and <b>free</b>.
 */
export function hostMemoryParts(host: HostMetrics, guestBytes: number | null, consumerBytes: number): HostMemoryPart[] {
  const used = host.memoryUsedBytes ?? 0;
  const consumers = Math.min(consumerBytes, used);
  const rest = used - consumers;
  return [
    ...ownMemoryParts(rest, guestBytes),
    { key: 'consumers', bytes: consumers },
    { key: 'reclaimable', bytes: host.memoryReclaimableBytes ?? 0 },
    { key: 'free', bytes: host.memoryFreeBytes ?? 0 },
  ];
}

/** The part of the used memory that is not a workload: the system, the VMs allocated to it, or both when they cannot be told apart. */
function ownMemoryParts(rest: number, guestBytes: number | null): HostMemoryPart[] {
  if (guestBytes === null) {
    return [{ key: 'system', bytes: rest }];
  }
  if (guestBytes > rest) {
    return [{ key: 'guestsSystem', bytes: rest }];
  }
  return [
    { key: 'system', bytes: rest - guestBytes },
    { key: 'guests', bytes: guestBytes },
  ];
}

/** What the running workloads of a machine use in memory, as their connector measures it. */
export function consumerMemory(machine: Machine): number {
  return machine.workloads.reduce((sum, workload) => sum + (workload.memoryBytes ?? 0), 0);
}
