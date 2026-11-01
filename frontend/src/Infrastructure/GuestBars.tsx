import ProgressBar from 'Components/ProgressBar/ProgressBar';
import type { Guest } from 'typings/Infrastructure';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import styles from './ConsumerBars.module.css';
import { machineAnchor, percent, share } from './machineUsage';

interface GuestBarsProps {
  guests: Guest[];
  cpuCapacity: number | null;
  memoryCapacity: number | null;
}

/**
 * The VMs of a hypervisor (D-59), like Unraid's VM list: the load each one reports from the inside and the
 * memory allocated to it, bars scaled to the heaviest. Each one opens its own card.
 */
/** A VM's load as it reports itself, with its share of the host when the host's cores are known. */
function guestCpuText(guest: Guest, cpuOfHost: number | null): string {
  if (guest.cpuCores === null) {
    return translate('GuestNotSampled');
  }
  const value =
    cpuOfHost === null
      ? formatCores(guest.cpuCores)
      : translate('CpuShareValue', { percent: percent(cpuOfHost), cores: formatCores(guest.cpuCores) });
  return translate(guest.scope === 'host' ? 'GuestLoad' : 'GuestLoadContainers', { value });
}

/** The memory allocated to a VM, with its share of the host's memory when known. */
function guestMemoryText(guest: Guest, memoryOfHost: number | null): string {
  if (guest.memoryCapacityBytes === null) {
    return '—';
  }
  const value =
    memoryOfHost === null
      ? formatBytes(guest.memoryCapacityBytes)
      : translate('MemoryShareValue', {
          bytes: formatBytes(guest.memoryCapacityBytes),
          percent: percent(memoryOfHost),
        });
  return translate('GuestAllocated', { value });
}

export default function GuestBars({ guests, cpuCapacity, memoryCapacity }: Readonly<GuestBarsProps>) {
  const cpuScale = Math.max(...guests.map((g) => g.cpuCores ?? 0), Number.EPSILON);
  const memoryScale = Math.max(...guests.map((g) => g.memoryCapacityBytes ?? 0), 1);

  return (
    <div>
      <h3 className={styles.heading}>{translate('VirtualMachines')}</h3>
      <ul className={styles.list}>
        {guests.map((guest) => {
          const cpuOfHost = share(guest.cpuCores, cpuCapacity);
          const memoryOfHost = share(guest.memoryCapacityBytes, memoryCapacity);
          const cpuText = guestCpuText(guest, cpuOfHost);
          const memoryText = guestMemoryText(guest, memoryOfHost);

          return (
            <li key={`${guest.environmentId}/${guest.machineId}`}>
              <a className={styles.consumer} href={`#${machineAnchor(guest.environmentId, guest.machineId)}`}>
                <span className={styles.name}>
                  {guest.name}
                  {guest.cpuCapacityCores ? (
                    <span className={styles.namespace}>
                      {translate('CoresCount', { count: guest.cpuCapacityCores })}
                    </span>
                  ) : null}
                </span>
                <span className={styles.metric}>
                  <ProgressBar
                    color="guests"
                    label={translate('CpuOf', { name: guest.name })}
                    progress={share(guest.cpuCores ?? 0, cpuScale) ?? 0}
                    valueText={cpuText}
                  />
                  <span className={styles.value}>{cpuText}</span>
                </span>
                <span className={styles.metric}>
                  <ProgressBar
                    color="memory"
                    label={translate('MemoryOf', { name: guest.name })}
                    progress={share(guest.memoryCapacityBytes ?? 0, memoryScale) ?? 0}
                    valueText={memoryText}
                  />
                  <span className={styles.value}>{memoryText}</span>
                </span>
              </a>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
