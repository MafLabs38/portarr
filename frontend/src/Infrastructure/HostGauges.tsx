import type { ReactNode } from 'react';

import ProgressBar from 'Components/ProgressBar/ProgressBar';
import type { HostMetrics, Machine } from 'typings/Infrastructure';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

import CoreBars from './CoreBars';
import styles from './MachineCard.module.css';
import { consumerMemory, type GuestTotals, type HostMemoryPart, hostMemoryParts, percent, share } from './machineUsage';
import MemoryDonut, { type MemorySegment } from './MemoryDonut';

interface HostGaugesProps {
  machine: Machine;
  host: HostMetrics;
  guests: GuestTotals | null;
  /** The CPU chart and its caption, drawn inside the CPU section, under the load figures. */
  chart: ReactNode;
}

const partTokens: Record<HostMemoryPart['key'], string> = {
  system: '--infoColor',
  guests: '--pink',
  guestsSystem: '--pink',
  consumers: '--purple',
  reclaimable: '--hairlineStrong',
  free: '--paperSunken',
};

const partClasses: Record<HostMemoryPart['key'], string | undefined> = {
  system: styles.legendSystem,
  guests: styles.legendGuests,
  guestsSystem: styles.legendGuests,
  consumers: styles.legendContainers,
  reclaimable: styles.legendReclaimable,
  free: styles.legendFree,
};

/** The CPU as text: warming up before the first measure, "used of capacity" when the cores are known, else the share. */
function hostCpuText(cpuPercent: number | null, cores: number | null, figure: string): string {
  if (cpuPercent === null) {
    return translate('SamplingWarmup');
  }
  return cores
    ? translate('CpuOfCapacity', { used: ((cpuPercent / 100) * cores).toFixed(2), capacity: cores })
    : figure;
}

/**
 * A machine as its host agent sees it (D-72), host truth first: overall load (100 − idle, I/O wait included)
 * with its I/O wait part, one bar per core, the memory ring whose parts sum to the total, and swap.
 */
export default function HostGauges({ machine, host, guests, chart }: Readonly<HostGaugesProps>) {
  const cores = host.cores ?? machine.cpuCapacityCores;
  const cpuFigure = host.cpuPercent === null ? '—' : `${Math.round(host.cpuPercent)} %`;
  const cpuText = hostCpuText(host.cpuPercent, cores, cpuFigure);
  const details = [
    host.load1 === null ? null : translate('LoadAverage', { value: host.load1.toFixed(2) }),
    host.temperatureCelsius === null
      ? null
      : translate('TemperatureValue', { value: Math.round(host.temperatureCelsius) }),
  ].filter(Boolean);

  const total = host.memoryTotalBytes;
  const parts = hostMemoryParts(host, guests ? (guests.memoryBytes ?? 0) : null, consumerMemory(machine));
  const segments: MemorySegment[] = parts
    .filter((part) => part.key !== 'free')
    .map((part) => ({ label: partLabel(part.key, machine, guests), bytes: part.bytes, token: partTokens[part.key] }));
  const usedShare = share(host.memoryUsedBytes, total);
  const memoryText =
    host.memoryUsedBytes === null
      ? translate('SamplingWarmup')
      : translate('MemoryOfLimit', { used: formatBytes(host.memoryUsedBytes), limit: formatBytes(total ?? 0) });

  const swapTotal = host.swapTotalBytes ?? 0;
  const swapUsed = host.swapUsedBytes ?? 0;
  const swapText = translate('MemoryOfLimit', { used: formatBytes(swapUsed), limit: formatBytes(swapTotal) });

  return (
    <>
      <section aria-label={translate('Cpu')} className={styles.section}>
        <div className={styles.figureRow}>
          <span className={styles.figureLabel}>{translate('Cpu')}</span>
          <span className={styles.figure}>{cpuFigure}</span>
          <span className={styles.figureText}>{cpuText}</span>
        </div>
        <ProgressBar
          label={translate('CpuOf', { name: machine.name })}
          progress={(host.cpuPercent ?? 0) / 100}
          size="medium"
          valueText={host.cpuPercent === null ? cpuText : cpuFigure}
        />
        <ul className={styles.stats}>
          <li className={styles.legendCpu}>
            {translate('HostLoad')} {cpuFigure}
          </li>
          <li className={styles.legendIowait}>
            {translate('Iowait')} {host.iowaitPercent === null ? '—' : `${host.iowaitPercent.toFixed(1)} %`}
          </li>
          {details.length > 0 ? <li>{details.join(' · ')}</li> : null}
        </ul>
        {chart}
        <CoreBars cores={host.corePercents} machineName={machine.name} />
      </section>

      <section aria-label={translate('Memory')} className={`${styles.section} ${styles.divided}`}>
        <div className={styles.figureRow}>
          <span className={styles.figureLabel}>{translate('Memory')}</span>
          <span className={styles.figureText}>{memoryText}</span>
        </div>
        <div className={styles.memory}>
          <MemoryDonut
            center={usedShare === null ? '—' : `${percent(usedShare)} %`}
            label={translate('MemoryChartLabel', { name: machine.name, value: memoryText })}
            segments={segments}
            total={total}
          />
          <ul className={styles.legend}>
            {parts.map((part) => (
              <li className={partClasses[part.key]} key={part.key}>
                <span className={styles.legendLabel}>{partLabel(part.key, machine, guests)}</span>
                <span className={styles.legendValue}>{formatBytes(part.bytes)}</span>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <div className={styles.swap}>
        <span className={styles.figureLabel}>{translate('Swap')}</span>
        {swapTotal > 0 ? (
          <>
            <ProgressBar
              color="swap"
              label={translate('SwapOf', { name: machine.name })}
              progress={swapUsed / swapTotal}
              valueText={swapText}
            />
            <span className={styles.swapValue}>{`${percent(swapUsed / swapTotal)} % · ${swapText}`}</span>
          </>
        ) : (
          <span className={styles.swapValue}>{translate('NoSwap')}</span>
        )}
      </div>
    </>
  );
}

function partLabel(key: HostMemoryPart['key'], machine: Machine, guests: GuestTotals | null): string {
  if (key === 'consumers') {
    return translate(machine.kind === 'kubernetes-node' ? 'Pods' : 'Containers');
  }

  if (key === 'guestsSystem') {
    return translate('MemoryPart_guestsSystem', { allocated: formatBytes(guests?.memoryBytes ?? 0) });
  }

  return translate(`MemoryPart_${key}`);
}
