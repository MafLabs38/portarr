import type { ReactNode } from 'react';

import ProgressBar from 'Components/ProgressBar/ProgressBar';
import type { Machine } from 'typings/Infrastructure';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import styles from './MachineCard.module.css';
import { type CpuSummary, type MemorySummary } from './machineCardView';
import { type GuestTotals, percent, share } from './machineUsage';
import MemoryDonut from './MemoryDonut';

interface ContainerGaugesProps {
  machine: Machine;
  guests: GuestTotals | null;
  scope: string;
  cpu: CpuSummary;
  memory: MemorySummary;
  /** The CPU chart and its caption. */
  chart: ReactNode;
}

const legendClass = {
  containers: styles.legendContainers,
  guests: styles.legendGuests,
  free: styles.legendFree,
} as const;

/** The VMs' load in cores, "≥" when a VM is a Docker host that only sees its containers. */
function guestLoadText(guests: GuestTotals, guestCpu: number | null): string {
  if (guestCpu === null) {
    return '—';
  }
  return `${guests.cpuLowerBound ? '≥ ' : ''}${formatCores(guestCpu)}`;
}

function CpuSection({ machine, guests, scope, cpu, chart }: Readonly<Omit<ContainerGaugesProps, 'memory'>>) {
  const guestCpu = cpu.guests;
  return (
    <section aria-label={translate('Cpu')} className={styles.section}>
      <div className={styles.figureRow}>
        <span className={styles.figureLabel}>{translate('Cpu')}</span>
        <span className={styles.figure}>{cpu.figure}</span>
        <span className={styles.figureText}>{cpu.text}</span>
      </div>
      <ProgressBar
        label={translate('CpuOf', { name: machine.name })}
        progress={share(cpu.own, machine.cpuCapacityCores) ?? 0}
        size="medium"
        stacked={guests ? (share(guestCpu, machine.cpuCapacityCores) ?? 0) : undefined}
        valueText={cpu.share === null ? cpu.text : cpu.figure}
      />
      <ul className={styles.stats}>
        <li className={styles.legendCpu}>
          {scope} {cpu.own === null ? '—' : formatCores(cpu.own)}
        </li>
        {guests ? (
          <li className={styles.legendGuests}>
            {translate('VmsGuestLoad')} {guestLoadText(guests, guestCpu)}
          </li>
        ) : null}
      </ul>
      {chart}
    </section>
  );
}

function MemorySection({ machine, memory }: Readonly<Pick<ContainerGaugesProps, 'machine' | 'memory'>>) {
  return (
    <section aria-label={translate('Memory')} className={`${styles.section} ${styles.divided}`}>
      <div className={styles.figureRow}>
        <span className={styles.figureLabel}>{translate('Memory')}</span>
        <span className={styles.figureText}>{memory.text}</span>
      </div>
      <div className={styles.memory}>
        <MemoryDonut
          center={memory.share === null ? '—' : `${percent(memory.share)} %`}
          label={translate('MemoryChartLabel', { name: machine.name, value: memory.text })}
          segments={memory.segments}
          total={memory.total}
        />
        <ul className={styles.legend}>
          {memory.legend.map((item) => (
            <li className={legendClass[item.kind]} key={item.label}>
              <span className={styles.legendLabel}>{item.label}</span>
              <span className={styles.legendValue}>{item.value}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

/** CPU then memory of a machine without a host agent: what its connector reports, and its VMs as they report themselves (D-59). */
export default function ContainerGauges({
  machine,
  guests,
  scope,
  cpu,
  memory,
  chart,
}: Readonly<ContainerGaugesProps>) {
  return (
    <>
      <CpuSection chart={chart} cpu={cpu} guests={guests} machine={machine} scope={scope} />
      <MemorySection machine={machine} memory={memory} />
    </>
  );
}
