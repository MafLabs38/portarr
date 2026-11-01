import { useState } from 'react';
import { Link } from 'react-router';

import ProgressBar from 'Components/ProgressBar/ProgressBar';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import styles from './ConsumerBars.module.css';
import { type Consumer, percent, share } from './machineUsage';

interface ConsumerBarsProps {
  consumers: Consumer[];
  cpuCapacity: number | null;
  memoryCapacity: number | null;
}

/** How many consumers a card shows before "Show all running". */
export const topCount = 8;

/**
 * The top consumers of a machine as horizontal bars (CPU, then memory), like Unraid's per-CPU bars. Bars are
 * scaled to the heaviest consumer of the machine, so small loads stay readable; the share of the machine is
 * written next to them. Each one opens its workload page.
 */
export default function ConsumerBars({ consumers, cpuCapacity, memoryCapacity }: Readonly<ConsumerBarsProps>) {
  const [showAll, setShowAll] = useState(false);
  const shown = showAll ? consumers : consumers.slice(0, topCount);
  const cpuScale = Math.max(...consumers.map((c) => c.cpuCores), Number.EPSILON);
  const memoryScale = Math.max(...consumers.map((c) => c.memoryBytes), 1);

  if (consumers.length === 0) {
    return <p className={styles.empty}>{translate('NoRunningWorkloads')}</p>;
  }

  return (
    <div>
      <h3 className={styles.heading}>{translate('TopConsumers')}</h3>
      <ul className={styles.list}>
        {shown.map(({ workload, cpuCores, memoryBytes }) => {
          const cpuOfMachine = share(cpuCores, cpuCapacity);
          const memoryOfMachine = share(memoryBytes, memoryCapacity);
          const cpuText =
            cpuOfMachine === null
              ? formatCores(cpuCores)
              : translate('CpuShareValue', { percent: percent(cpuOfMachine), cores: formatCores(cpuCores) });
          const memoryText =
            memoryOfMachine === null
              ? formatBytes(memoryBytes)
              : translate('MemoryShareValue', { bytes: formatBytes(memoryBytes), percent: percent(memoryOfMachine) });

          return (
            <li key={workload.id}>
              <Link
                className={styles.consumer}
                to={`/workloads/${encodeURIComponent(workload.environmentId)}/${encodeURIComponent(workload.name)}`}
              >
                <span className={styles.name}>
                  {workload.name}
                  {workload.namespace ? <span className={styles.namespace}>{workload.namespace}</span> : null}
                </span>
                <span className={styles.metric}>
                  <ProgressBar
                    label={translate('CpuOf', { name: workload.name })}
                    progress={share(cpuCores, cpuScale) ?? 0}
                    valueText={cpuText}
                  />
                  <span className={styles.value}>{cpuText}</span>
                </span>
                <span className={styles.metric}>
                  <ProgressBar
                    color="memory"
                    label={translate('MemoryOf', { name: workload.name })}
                    progress={share(memoryBytes, memoryScale) ?? 0}
                    valueText={memoryText}
                  />
                  <span className={styles.value}>{memoryText}</span>
                </span>
              </Link>
            </li>
          );
        })}
      </ul>
      {consumers.length > topCount ? (
        <button className={styles.toggle} onClick={() => setShowAll(!showAll)} type="button">
          {showAll
            ? translate('ShowTop', { count: topCount })
            : translate('ShowAllRunning', { count: consumers.length })}
        </button>
      ) : null}
    </div>
  );
}
