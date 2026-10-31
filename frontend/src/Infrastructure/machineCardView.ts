import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import type { CpuChartPoint } from './CpuChart';
import { type GuestTotals, percent, share } from './machineUsage';
import type { MemorySegment } from './MemoryDonut';

/** The figures a card shows for a machine that has no host agent: what its connector reports (and its VMs, D-59). */
export interface CpuSummary {
  own: number | null;
  guests: number | null;
  total: number | null;
  /** The load as a share of the capacity, null when unknown. */
  share: number | null;
  /** "42 %", "≥ 42 %" when a VM is a Docker host (a lower bound), "—" when unknown. */
  figure: string;
  text: string;
}

export function cpuSummary(machine: Machine, guests: GuestTotals | null): CpuSummary {
  const own = machine.cpuCores;
  const guestCpu = guests?.cpuCores ?? null;
  const total = own === null && guestCpu === null ? null : (own ?? 0) + (guestCpu ?? 0);
  const loadShare = share(total, machine.cpuCapacityCores);
  const lowerBound = guests?.cpuLowerBound ? '≥ ' : '';
  const figure = loadShare === null ? '—' : `${lowerBound}${percent(loadShare)} %`;
  return { own, guests: guestCpu, total, share: loadShare, figure, text: cpuText(total, machine.cpuCapacityCores) };
}

function cpuText(total: number | null, capacity: number | null): string {
  if (total === null) {
    return translate('SamplingWarmup');
  }
  return capacity ? translate('CpuOfCapacity', { used: total.toFixed(2), capacity }) : formatCores(total);
}

export interface MemorySummary {
  segments: MemorySegment[];
  total: number | null;
  used: number;
  share: number | null;
  text: string;
  /** The legend: what the machine's memory is made of, by kind (the card maps a kind to its color). */
  legend: { kind: 'containers' | 'guests' | 'free'; label: string; value: string }[];
}

export function memorySummary(machine: Machine, guests: GuestTotals | null, scope: string): MemorySummary {
  const total = machine.memoryCapacityBytes;
  const segments: MemorySegment[] = [{ label: scope, bytes: machine.memoryBytes ?? 0, token: '--purple' }];
  if (guests) {
    segments.push({ label: translate('VirtualMachines'), bytes: guests.memoryBytes ?? 0, token: '--pink' });
  }
  const used = segments.reduce((sum, s) => sum + s.bytes, 0);

  const legend: MemorySummary['legend'] = [
    { kind: 'containers', label: scope, value: formatBytes(machine.memoryBytes ?? 0) },
  ];
  if (guests) {
    legend.push({ kind: 'guests', label: translate('VmsAllocated'), value: formatBytes(guests.memoryBytes ?? 0) });
  }
  if (total !== null) {
    const freeLabel = translate(machine.scope === 'containers' && !guests ? 'MemoryFreeOrOther' : 'MemoryFree');
    legend.push({ kind: 'free', label: freeLabel, value: formatBytes(Math.max(total - used, 0)) });
  }

  return {
    segments,
    total,
    used,
    share: machine.memoryBytes === null ? null : share(used, total),
    text: memoryText(machine.memoryBytes, used, total),
    legend,
  };
}

function memoryText(memoryBytes: number | null, used: number, total: number | null): string {
  if (memoryBytes === null) {
    return translate('SamplingWarmup');
  }
  return total ? translate('MemoryOfLimit', { used: formatBytes(used), limit: formatBytes(total) }) : formatBytes(used);
}

/** "Docker host · hypervisor · 8 cores · 62.7 GB": what the machine is. */
export function machineMeta(machine: Machine): string {
  return [
    translate(`MachineKind_${machine.kind}`),
    ...machine.roles,
    machine.cpuCapacityCores ? translate('CoresCount', { count: machine.cpuCapacityCores }) : null,
    machine.memoryCapacityBytes ? formatBytes(machine.memoryCapacityBytes) : null,
  ]
    .filter(Boolean)
    .join(' · ');
}

/** A value of the container view's chart: % of the machine when its cores are known, else cores. */
function toChart(cores: number | null, capacity: number | null): number | null {
  if (cores === null) {
    return null;
  }
  return capacity ? (cores / capacity) * 100 : cores;
}

/** The points of the CPU chart: the host agent's own (load and I/O wait), else the connector's (the machine and its VMs). */
export function chartPoints(host: HostMetrics | null, machine: Machine): CpuChartPoint[] {
  if (host) {
    return host.history.map((point) => ({ at: point.at, value: point.cpuPercent, extra: point.iowaitPercent }));
  }
  return machine.history.map((point) => ({
    at: point.at,
    value: toChart(point.cpuCores, machine.cpuCapacityCores),
    extra: toChart(point.guestCpuCores, machine.cpuCapacityCores),
  }));
}

/** The caption under the chart: the window, then how the figures were read. */
export function chartCaption(
  host: HostMetrics | null,
  environment: InfrastructureEnvironment,
  scope: string,
  hasGuests: boolean,
): string {
  const window = translate('LiveWindow', { minutes: Math.round(environment.windowSeconds / 60) });
  if (host) {
    return [window, translate('HostAgentNote', { seconds: host.intervalSeconds })].join(' · ');
  }
  const cadence =
    environment.resolutionSeconds === null
      ? translate('SampledEvery', { seconds: environment.intervalSeconds })
      : translate('MetricsResolution', { seconds: environment.resolutionSeconds });
  return [window, cadence, hasGuests ? translate('GuestReportedNote') : scope].join(' · ');
}

/** The second series of the chart: I/O wait with a host agent, the VMs' load on a hypervisor. */
export function chartExtra(host: HostMetrics | null, hasGuests: boolean): 'iowait' | 'guests' | undefined {
  if (host) {
    return 'iowait';
  }
  return hasGuests ? 'guests' : undefined;
}
