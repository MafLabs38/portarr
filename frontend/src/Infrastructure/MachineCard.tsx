import { memo } from 'react';

import * as icons from 'Helpers/Props/icons';
import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import type Workload from 'typings/Workload';
import translate from 'Utilities/String/translate';

import ConsumerBars from './ConsumerBars';
import ContainerGauges from './ContainerGauges';
import GuestBars from './GuestBars';
import HostGauges from './HostGauges';
import styles from './MachineCard.module.css';
import { cpuSummary, memorySummary } from './machineCardView';
import MachineChart from './MachineChart';
import MachineHeader from './MachineHeader';
import { consumers, guestTotals, machineAnchor, mergeHost } from './machineUsage';

interface MachineCardProps {
  environment: InfrastructureEnvironment;
  machine: Machine;
  /** The environment's workloads by id. */
  workloads: Map<string, Workload>;
  /** Host agent reads streamed since the snapshot (D-72). */
  hostReads?: HostMetrics[];
}

/**
 * One machine, Unraid-dashboard style and compact: CPU (load bar and live chart), memory ring, then the VMs
 * it hosts and its top consumers. With a host agent (node_exporter, D-72) the figures are the host's own,
 * every second: load per core, I/O wait, memory parts that sum to the total, swap. Without one, a
 * hypervisor's figures add its VMs as they report themselves (D-59), labelled as guest-reported, and the
 * host's own processes are not measured (D-57).
 */
function MachineCard({ environment, machine, workloads, hostReads }: Readonly<MachineCardProps>) {
  const agent = mergeHost(machine.hostMetrics, hostReads, environment.windowSeconds);
  const host = agent?.sampledAt ? agent : null;
  const scope = translate(`UsageScope_${machine.scope}`);
  const guests = guestTotals(machine);
  const cpu = cpuSummary(machine, guests);
  const memory = memorySummary(machine, guests, scope);

  const chart = (
    <MachineChart
      cpuText={cpu.text}
      environment={environment}
      hasGuests={guests !== null}
      host={host}
      machine={machine}
      scope={scope}
    />
  );

  return (
    <article aria-label={machine.name} className={styles.card} id={machineAnchor(environment.id, machine.id)}>
      <MachineHeader
        agent={agent}
        environment={environment}
        host={host}
        icon={machine.kind === 'docker-host' ? icons.CONTAINER : icons.NODE}
        isHypervisor={guests !== null}
        machine={machine}
      />

      {host ? (
        <HostGauges chart={chart} guests={guests} host={host} machine={machine} />
      ) : (
        <ContainerGauges chart={chart} cpu={cpu} guests={guests} machine={machine} memory={memory} scope={scope} />
      )}

      {machine.guests.length > 0 ? (
        <GuestBars
          cpuCapacity={machine.cpuCapacityCores}
          guests={machine.guests}
          memoryCapacity={machine.memoryCapacityBytes}
        />
      ) : null}

      <ConsumerBars
        consumers={consumers(machine, workloads)}
        cpuCapacity={machine.cpuCapacityCores}
        memoryCapacity={machine.memoryCapacityBytes}
      />
    </article>
  );
}

/** Memoized: a streamed read re-renders only the cards whose own reads, snapshot or workloads changed. */
export default memo(MachineCard);
