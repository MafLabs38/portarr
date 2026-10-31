import type { InfrastructureEnvironment } from 'typings/Infrastructure';
import translate from 'Utilities/String/translate';

import styles from './InfrastructureIndex.module.css';

interface EnvironmentSummaryProps {
  environment: InfrastructureEnvironment;
  /** Workloads of the environment (for a hosted one, its sites and workers). */
  workloadCount: number;
}

/**
 * An environment as a light header line: its name, connector and machines, or why it shows none (not
 * connected, unreachable, hosted service). Its machines are cards in the shared grid.
 */
export default function EnvironmentSummary({ environment, workloadCount }: Readonly<EnvironmentSummaryProps>) {
  const machines = environment.machines.length;
  let detail: string;
  if (environment.error) {
    detail = translate('MetricsUnavailable', { reason: translate(`Reason_${environment.error}`) });
  } else if (environment.runtime === 'cloudflare') {
    detail = translate('HostedEnvironment', { count: workloadCount });
  } else if (machines > 0) {
    detail = translate(machines === 1 ? 'MachineCountOne' : 'MachineCount', { count: machines });
  } else {
    detail = translate(environment.runtime === 'none' ? 'NoConnector' : 'SamplingWarmup');
  }

  return (
    <li aria-label={environment.name} className={environment.error ? styles.environmentError : styles.environment}>
      <span className={styles.environmentName}>{environment.name}</span>
      <span className={styles.meta}>
        {translate(`Runtime_${environment.runtime}`)} · {detail}
      </span>
    </li>
  );
}
