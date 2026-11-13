import { Link } from 'react-router';

import ProgressBar from 'Components/ProgressBar/ProgressBar';
import * as icons from 'Helpers/Props/icons';
import type { Gpu, GpuDevice, InfrastructureEnvironment } from 'typings/Infrastructure';
import type Workload from 'typings/Workload';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';

import CpuChart from './CpuChart';
import styles from './GpuBox.module.css';

interface GpuBoxProps {
  gpu: Gpu;
  environment: InfrastructureEnvironment;
  /** The environment's workloads by id, to name the ones that have a card attached. */
  workloads: Map<string, Workload>;
}

interface Load {
  key: string;
  label: string;
  percent: number | null;
  text: string;
}

/** The loads a card reports, each as a bar: the card itself, its memory, and the video encoder and decoder a media server uses. */
function loadsOf(device: GpuDevice): Load[] {
  const memoryShare =
    device.memoryUsedBytes !== null && device.memoryTotalBytes
      ? (device.memoryUsedBytes / device.memoryTotalBytes) * 100
      : null;
  const memoryText =
    device.memoryUsedBytes === null
      ? '—'
      : translate('MemoryOfLimit', {
          used: formatBytes(device.memoryUsedBytes),
          limit: formatBytes(device.memoryTotalBytes ?? 0),
        });
  const percentText = (value: number | null) => (value === null ? '—' : `${Math.round(value)} %`);
  return [
    {
      key: 'load',
      label: translate('GpuLoad'),
      percent: device.utilizationPercent,
      text: percentText(device.utilizationPercent),
    },
    { key: 'memory', label: translate('Memory'), percent: memoryShare, text: memoryText },
    {
      key: 'encoder',
      label: translate('GpuEncoder'),
      percent: device.encoderPercent,
      text: percentText(device.encoderPercent),
    },
    {
      key: 'decoder',
      label: translate('GpuDecoder'),
      percent: device.decoderPercent,
      text: percentText(device.decoderPercent),
    },
  ];
}

/** Temperature, power and encoder sessions as the small print of a card. */
function statsOf(device: GpuDevice): string[] {
  const stats: string[] = [];
  if (device.temperatureCelsius !== null) {
    stats.push(translate('GpuTemperature', { value: Math.round(device.temperatureCelsius) }));
  }
  if (device.powerWatts !== null) {
    stats.push(
      device.powerLimitWatts === null
        ? translate('GpuPower', { watts: Math.round(device.powerWatts) })
        : translate('GpuPowerOfLimit', {
            watts: Math.round(device.powerWatts),
            limit: Math.round(device.powerLimitWatts),
          }),
    );
  }
  if (device.encoderSessions !== null && device.encoderSessions > 0) {
    stats.push(
      translate(device.encoderSessions === 1 ? 'GpuOneSession' : 'GpuSessions', { count: device.encoderSessions }),
    );
  }
  return stats;
}

/**
 * The graphics card of a machine (D-120), under its CPU and memory: the card's load, memory, video encoder and decoder as bars, its
 * temperature and power, its load over the live window, and the workloads created with a card attached. Portarr does not measure a
 * card's load per workload: it says which ones can use it.
 */
export default function GpuBox({ gpu, environment, workloads }: Readonly<GpuBoxProps>) {
  const users = gpu.workloadIds
    .map((id) => workloads.get(id))
    .filter((workload): workload is Workload => workload !== undefined);

  return (
    <section aria-label={translate('Gpu')} className={styles.box}>
      {gpu.devices.map((device) => (
        <div className={styles.device} key={device.id}>
          <div className={styles.header}>
            <icons.GPU aria-hidden="true" className={styles.icon} size={16} strokeWidth={2.25} />
            <h3 className={styles.name}>{device.name}</h3>
            {device.driver ? (
              <span className={styles.muted}>{translate('GpuDriver', { version: device.driver })}</span>
            ) : null}
          </div>
          <ul className={styles.bars}>
            {loadsOf(device).map((load) => (
              <li className={styles.bar} key={load.key}>
                <span className={styles.barLabel}>{load.label}</span>
                <ProgressBar
                  color={load.key === 'memory' ? 'memory' : 'cpu'}
                  label={translate('GpuOf', { what: load.label, name: device.name })}
                  progress={(load.percent ?? 0) / 100}
                  valueText={load.text}
                />
                <span className={styles.value}>{load.text}</span>
              </li>
            ))}
          </ul>
          <CpuChart
            label={translate('GpuChartLabel', {
              name: device.name,
              minutes: Math.round(environment.windowSeconds / 60),
            })}
            percent
            points={device.history.map((point) => ({ at: point.at, value: point.utilizationPercent, extra: null }))}
            windowSeconds={environment.windowSeconds}
          />
          <ul className={styles.stats}>
            {statsOf(device).map((stat) => (
              <li key={stat}>{stat}</li>
            ))}
          </ul>
        </div>
      ))}
      {gpu.error ? (
        <p className={styles.error}>{translate('GpuUnavailable', { reason: translate(`Reason_${gpu.error}`) })}</p>
      ) : null}
      {gpu.devices.length > 0 ? (
        <div className={styles.users}>
          <span>{translate('GpuUsedBy')}</span>
          {users.length === 0 ? <span>{translate('GpuUsedByNone')}</span> : null}
          {users.map((workload) => (
            <Link
              className={styles.user}
              key={workload.id}
              to={`/workloads/${encodeURIComponent(workload.environmentId)}/${encodeURIComponent(workload.name)}`}
            >
              {workload.name}
            </Link>
          ))}
        </div>
      ) : null}
    </section>
  );
}
