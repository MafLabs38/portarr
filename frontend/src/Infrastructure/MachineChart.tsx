import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import translate from 'Utilities/String/translate';

import CpuChart from './CpuChart';
import styles from './MachineCard.module.css';
import { chartCaption, chartExtra, chartPoints } from './machineCardView';

interface MachineChartProps {
  environment: InfrastructureEnvironment;
  machine: Machine;
  host: HostMetrics | null;
  /** The CPU as text, for the chart's accessible label when there is no host reading. */
  cpuText: string;
  scope: string;
  hasGuests: boolean;
}

/** The live CPU chart of a machine with its caption (window, cadence and where the figures come from). */
export default function MachineChart({
  environment,
  machine,
  host,
  cpuText,
  scope,
  hasGuests,
}: Readonly<MachineChartProps>) {
  const value = host?.cpuPercent == null ? cpuText : `${Math.round(host.cpuPercent)} %`;

  return (
    <>
      <CpuChart
        extra={chartExtra(host, hasGuests)}
        label={translate('CpuChartLabel', {
          name: machine.name,
          minutes: Math.round(environment.windowSeconds / 60),
          value,
        })}
        percent={host !== null || Boolean(machine.cpuCapacityCores)}
        points={chartPoints(host, machine)}
        windowSeconds={environment.windowSeconds}
      />
      <span className={styles.caption}>{chartCaption(host, environment, scope, hasGuests)}</span>
    </>
  );
}
