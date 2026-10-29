import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import type { AppWorkload } from 'typings/App';
import translate from 'Utilities/String/translate';

import styles from './WorkloadSummary.module.css';

interface WorkloadSummaryProps {
  workloads: AppWorkload[];
}

type Tone = 'ok' | 'bad' | 'warn' | 'muted';

// Docker appends the healthcheck to the status; it has its own line on the tile (D-45).
const healthSuffixes = [' (healthy)', ' (unhealthy)', ' (health: starting)'];

// "Up 3 hours (healthy)" → "Up 3 hours". Plain suffix checks: no regex backtracking on Docker's text.
function stateText(workload: AppWorkload): string {
  if (!workload.found) {
    return translate('ContainerNotFound');
  }
  const text = workload.statusText ?? workload.state ?? '';
  const suffix = healthSuffixes.find((s) => text.endsWith(s));
  return suffix ? text.slice(0, -suffix.length) : text;
}

function stateTone(workload: AppWorkload): Tone {
  if (!workload.found || workload.state === 'exited' || workload.state === 'dead' || workload.state === 'restarting') {
    return 'bad';
  }
  return workload.state === 'running' ? 'ok' : 'muted';
}

// One line of the tile tooltip: "web: Up 3 hours (healthy)".
function describe(workload: AppWorkload): string {
  const health = workload.health && workload.health !== 'none' ? ` (${workload.health})` : '';
  return `${workload.name}: ${stateText(workload)}${health}`;
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
function Line({ icon: Icon, label, value, tone, title }: Readonly<LineProps>) {
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
export default function WorkloadSummary({ workloads }: Readonly<WorkloadSummaryProps>) {
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
  const details = workloads.map(describe).join('\n');

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
