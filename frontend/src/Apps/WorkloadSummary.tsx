import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import type { AppWorkload } from 'typings/App';
import translate from 'Utilities/String/translate';

import styles from './WorkloadSummary.module.css';

interface WorkloadSummaryProps {
  workloads: AppWorkload[];
}

type Tone = 'ok' | 'bad' | 'warn' | 'muted';

// "Up 3 hours (healthy)" → "Up 3 hours": the healthcheck has its own line (D-45).
function stateText(workload: AppWorkload): string {
  if (!workload.found) {
    return translate('ContainerNotFound');
  }
  return (workload.statusText ?? workload.state ?? '').replace(/\s*\((healthy|unhealthy|health: starting)\)$/, '');
}

function stateTone(workload: AppWorkload): Tone {
  if (!workload.found || workload.state === 'exited' || workload.state === 'dead' || workload.state === 'restarting') {
    return 'bad';
  }
  return workload.state === 'running' ? 'ok' : 'muted';
}

const healthTone: Record<string, Tone> = { healthy: 'ok', unhealthy: 'bad', starting: 'warn', none: 'muted' };

interface LineProps {
  icon: LucideIcon;
  label: string;
  value: string;
  tone: Tone;
  title?: string;
}

// Icon instead of a text label, so the value keeps the tile's width; the label stays accessible.
function Line({ icon: Icon, label, value, tone, title }: LineProps) {
  return (
    <div className={`${styles.line} ${styles[tone]}`} title={title ?? `${label}: ${value}`}>
      <Icon aria-label={label} role="img" size={13} strokeWidth={2.5} />
      <span className={styles.value}>{value}</span>
    </div>
  );
}

/** Several workloads: unhealthy first, then starting, then healthy; none means no healthcheck at all. */
function healthSummary(workloads: AppWorkload[]): { tone: Tone; value: string } {
  const count = (health: string) => workloads.filter((w) => w.health === health).length;
  const unhealthy = count('unhealthy');
  const starting = count('starting');
  const healthy = count('healthy');

  if (unhealthy > 0) {
    return { tone: 'bad', value: translate('HealthUnhealthyCount', { count: unhealthy }) };
  }
  if (starting > 0) {
    return { tone: 'warn', value: translate('HealthStartingCount', { count: starting }) };
  }
  if (healthy > 0) {
    return { tone: 'ok', value: translate('HealthHealthyCount', { count: healthy }) };
  }
  return { tone: 'muted', value: translate('HealthNone') };
}

/** Container state and healthcheck, like Portainer (D-45); several workloads are counted. */
export default function WorkloadSummary({ workloads }: WorkloadSummaryProps) {
  if (workloads.length === 0) {
    return null;
  }

  const [only] = workloads;
  if (workloads.length === 1 && only) {
    const health = only.health ?? 'none';
    return (
      <div className={styles.summary}>
        <Line
          icon={icons.CONTAINER}
          label={translate('Container')}
          title={only.statusText ?? undefined}
          tone={stateTone(only)}
          value={stateText(only)}
        />
        {only.found ? (
          <Line
            icon={icons.HEALTHCHECK}
            label={translate('Healthcheck')}
            tone={healthTone[health] ?? 'muted'}
            value={translate(`Health${health.charAt(0).toUpperCase()}${health.slice(1)}`)}
          />
        ) : null}
      </div>
    );
  }

  const up = workloads.filter((w) => stateTone(w) === 'ok').length;
  const health = healthSummary(workloads);
  const details = workloads
    .map((w) => `${w.name}: ${stateText(w)}${w.health && w.health !== 'none' ? ` (${w.health})` : ''}`)
    .join('\n');

  return (
    <div className={styles.summary} title={details}>
      <Line
        icon={icons.CONTAINER}
        label={translate('Containers')}
        tone={up === workloads.length ? 'ok' : 'bad'}
        value={translate('ContainersUp', { up, total: workloads.length })}
      />
      <Line icon={icons.HEALTHCHECK} label={translate('Healthcheck')} tone={health.tone} value={health.value} />
    </div>
  );
}
