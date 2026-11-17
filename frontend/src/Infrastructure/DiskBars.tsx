import ProgressBar from 'Components/ProgressBar/ProgressBar';
import type { HostDisk, Machine } from 'typings/Infrastructure';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

import styles from './DiskBars.module.css';
import { percent } from './machineUsage';

/** A disk this full is worth a look (the free space of a data disk is what runs out); a display threshold, the alerts stay with the tools. */
const WARNING_SHARE = 0.9;
const DANGER_SHARE = 0.95;

export function diskColor(usedShare: number): 'disk' | 'warning' | 'danger' {
  if (usedShare >= DANGER_SHARE) {
    return 'danger';
  }
  return usedShare >= WARNING_SHARE ? 'warning' : 'disk';
}

interface DiskBarsProps {
  machine: Machine;
  disks: HostDisk[];
}

/** One saturation gauge per filesystem of the machine: how much of it is used, turning orange then red as it fills up. */
export default function DiskBars({ machine, disks }: Readonly<DiskBarsProps>) {
  if (disks.length === 0) {
    return null;
  }

  return (
    <section aria-label={translate('Disks')} className={styles.disks}>
      <span className={styles.heading}>{translate('Disks')}</span>
      {disks.map((disk) => {
        const used = disk.sizeBytes > 0 ? disk.usedBytes / disk.sizeBytes : 0;
        const text = translate('DiskUsage', {
          used: formatBytes(disk.usedBytes),
          size: formatBytes(disk.sizeBytes),
          free: formatBytes(Math.max(disk.sizeBytes - disk.usedBytes, 0)),
        });
        return (
          <div className={styles.row} key={`${disk.device}:${disk.mount}`}>
            <span className={styles.mount} title={`${disk.device} · ${disk.fsType}`}>
              {disk.mount}
            </span>
            <ProgressBar
              color={diskColor(used)}
              label={translate('DiskOf', { mount: disk.mount, name: machine.name })}
              progress={used}
              valueText={`${percent(used)} %`}
            />
            <span className={styles.value}>{`${percent(used)} % · ${text}`}</span>
          </div>
        );
      })}
    </section>
  );
}
