import type { LucideIcon } from 'lucide-react';

import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import translate from 'Utilities/String/translate';

import LiveIndicator from './LiveIndicator';
import styles from './MachineCard.module.css';
import { machineMeta } from './machineCardView';

interface MachineHeaderProps {
  environment: InfrastructureEnvironment;
  machine: Machine;
  icon: LucideIcon;
  /** The host agent reading when it is usable (the card then shows the host's own figures). */
  host: HostMetrics | null;
  /** The host agent's last answer, usable or not: its error is shown. */
  agent: HostMetrics | null;
  isHypervisor: boolean;
}

function HypervisorBadge({ count }: Readonly<{ count: number }>) {
  return (
    <span className={styles.badge}>{translate(count === 1 ? 'HypervisorOneVm' : 'HypervisorVms', { count })}</span>
  );
}

function LiveAge({
  environment,
  machine,
  host,
}: Readonly<Pick<MachineHeaderProps, 'environment' | 'machine' | 'host'>>) {
  if (host) {
    return <LiveIndicator at={host.sampledAt} expectedSeconds={host.intervalSeconds} />;
  }
  const lastAt = machine.history.at(-1)?.at ?? null;
  return (
    <LiveIndicator
      at={environment.error ? null : lastAt}
      expectedSeconds={environment.resolutionSeconds ?? environment.intervalSeconds}
    />
  );
}

/** The card's header: icon, name, badges (VM on, hypervisor, node not ready), the age of the last sample, what it is, and why a reading is missing. */
export default function MachineHeader({
  environment,
  machine,
  icon: Icon,
  host,
  agent,
  isHypervisor,
}: Readonly<MachineHeaderProps>) {
  const meta = machineMeta(machine);

  return (
    <>
      <header className={styles.header}>
        <Icon aria-hidden="true" className={styles.icon} size={18} strokeWidth={2.25} />
        <h2 className={styles.title}>{machine.name}</h2>
        {machine.host ? <span className={styles.badge}>{translate('VmOn', { host: machine.host.name })}</span> : null}
        {isHypervisor ? <HypervisorBadge count={machine.guests.length} /> : null}
        {machine.ready === false ? <span className={styles.notReady}>{translate('NodeNotReady')}</span> : null}
        <span className={styles.live}>
          <LiveAge environment={environment} host={host} machine={machine} />
        </span>
      </header>
      <span className={styles.meta}>{machine.kind === 'kubernetes-node' ? `${environment.name} · ${meta}` : meta}</span>
      {environment.error ? (
        <p className={styles.error}>
          {translate('MetricsUnavailable', { reason: translate(`Reason_${environment.error}`) })}
        </p>
      ) : null}
      {agent?.error ? (
        <p className={styles.error}>
          {translate(host ? 'HostAgentFailing' : 'HostAgentUnavailable', {
            reason: translate(`Reason_${agent.error}`),
          })}
        </p>
      ) : null}
    </>
  );
}
