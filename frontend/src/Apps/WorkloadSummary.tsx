import * as icons from 'Helpers/Props/icons';
import type { AppWorkload } from 'typings/App';
import translate from 'Utilities/String/translate';

import SummaryLine, { type Tone } from './SummaryLine';
import styles from './WorkloadSummary.module.css';

interface WorkloadSummaryProps {
  workloads: AppWorkload[];
}

// Docker appends the healthcheck to the status; it has its own line on the tile (D-45).
const healthSuffixes = [' (healthy)', ' (unhealthy)', ' (health: starting)'];

// A container that is not in the inventory because its environment could not be read is unknown, not missing (the server says so:
// the workload's status is Unknown, with the environment that cannot be reached as its reason).
function isUnread(workload: AppWorkload): boolean {
  return !workload.found && workload.status.status === 'Unknown';
}

// "Up 3 hours (healthy)" → "Up 3 hours". Plain suffix checks: no regex backtracking on Docker's text.
function stateText(workload: AppWorkload): string {
  if (isUnread(workload)) {
    return translate('CannotBeRead');
  }
  if (!workload.found) {
    return translate(workload.runtime === 'cloudflare' ? 'DeploymentNotFound' : 'ContainerNotFound');
  }
  const text = workload.statusText ?? workload.state ?? '';
  const suffix = healthSuffixes.find((s) => text.endsWith(s));
  return suffix ? text.slice(0, -suffix.length) : text;
}

function stateTone(workload: AppWorkload): Tone {
  if (isUnread(workload)) {
    return 'warn';
  }
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

// All up: ok. The others only unread (their environment cannot be reached): a warning, not an alarm. Any other missing one: bad.
function upTone(up: number, unread: number, total: number): Tone {
  if (up === total) {
    return 'ok';
  }
  return up + unread === total ? 'warn' : 'bad';
}

/** Container state and healthcheck, like Portainer (D-45); several workloads are counted. */
export default function WorkloadSummary({ workloads }: Readonly<WorkloadSummaryProps>) {
  if (workloads.length === 0) {
    return null;
  }

  const [only] = workloads;
  if (workloads.length === 1 && only) {
    const health = only.health ?? 'none';
    // A CDN deployment has no container healthcheck: the HTTP check line says whether it answers.
    const container = only.runtime !== 'cloudflare';
    return (
      <div className={styles.summary}>
        <SummaryLine
          icon={container ? icons.CONTAINER : icons.DEPLOYMENT}
          label={translate(container ? 'Container' : 'Deployment')}
          title={only.statusText ?? (isUnread(only) ? only.status.reason : undefined)}
          tone={stateTone(only)}
          value={stateText(only)}
        />
        {only.found && container ? (
          <SummaryLine
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
  const unread = workloads.filter(isUnread).length;
  const health = healthSummary(workloads);
  const details = workloads.map(describe).join('\n');

  return (
    <div className={styles.summary} title={details}>
      <SummaryLine
        icon={icons.CONTAINER}
        label={translate('Containers')}
        tone={upTone(up, unread, workloads.length)}
        value={translate(unread > 0 ? 'ContainersUpUnread' : 'ContainersUp', { up, total: workloads.length, unread })}
      />
      <SummaryLine icon={icons.HEALTHCHECK} label={translate('Healthcheck')} tone={health.tone} value={health.value} />
    </div>
  );
}
