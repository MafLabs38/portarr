import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { McpServer, McpStatus } from 'typings/Mcp';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

export interface McpHealthDescription {
  kind: kinds.Kind;
  icon: LucideIcon;
  /** "Healthy", "Down for 12 days": the state first, never color alone (DESIGN.md §5). */
  label: string;
  /** When it was last seen healthy, or why it is down: what the operator wonders next. */
  detail: string | null;
}

/** "5 min", "3 h", "12 days": how long ago a date is, as a plain duration. */
export function formatElapsed(at: string, now: number = Date.now()): string {
  const minutes = Math.max(1, Math.floor((now - Date.parse(at)) / 60_000));
  if (minutes < 60) {
    return translate('DurationMinutes', { count: minutes });
  }
  const hours = Math.floor(minutes / 60);
  return hours < 48
    ? translate('DurationHours', { count: hours })
    : translate('DurationDays', { count: Math.floor(hours / 24) });
}

const reasons: Record<string, string> = {
  timeout: 'McpReasonTimeout',
  'dns-failure': 'McpReasonDns',
  'connection-failed': 'McpReasonConnection',
  'tls-invalid': 'McpReasonTls',
  'tls-expired': 'McpReasonTlsExpired',
  'invalid-response': 'McpReasonNotMcp',
  'check-failed': 'McpReasonCheckFailed',
};

function describeReason(error: string | null): string | null {
  if (error === null) {
    return null;
  }
  const key = reasons[error];
  if (key) {
    return translate(key);
  }
  return error.startsWith('http-') ? translate('McpReasonHttp', { code: error.slice(5) }) : error;
}

const statusKinds: Record<McpStatus, { kind: kinds.Kind; icon: LucideIcon }> = {
  healthy: { kind: kinds.SUCCESS, icon: icons.HEALTHY },
  'authentication-required': { kind: kinds.SUCCESS, icon: icons.HEALTHY },
  down: { kind: kinds.DANGER, icon: icons.DOWN },
  waiting: { kind: kinds.DISABLED, icon: icons.UNKNOWN },
  'not-checked': { kind: kinds.DISABLED, icon: icons.UNKNOWN },
};

/**
 * What the pages say about a server's health (V9, D-114): healthy and since when it was last seen, or down and
 * for how long, with the last time it was healthy. A local server is not checked, which is not a fault.
 */
export default function describeMcpHealth(server: McpServer, now: number = Date.now()): McpHealthDescription {
  const { kind, icon } = statusKinds[server.status];
  const health = server.health;

  switch (server.status) {
    case 'healthy':
      return {
        kind,
        icon,
        label: translate('McpHealthy'),
        detail: health ? translate('McpSeen', { age: formatAge(health.lastCheckedAt, now) }) : null,
      };
    case 'authentication-required':
      return {
        kind,
        icon,
        label: translate('McpHealthyAuth'),
        detail: health ? translate('McpSeen', { age: formatAge(health.lastCheckedAt, now) }) : null,
      };
    case 'down': {
      const seen = health?.lastHealthyAt
        ? translate('McpLastHealthy', { age: formatAge(health.lastHealthyAt, now) })
        : translate('McpNeverHealthy');
      const reason = describeReason(health?.error ?? null);
      return {
        kind,
        icon,
        label: health ? translate('McpDownFor', { duration: formatElapsed(health.since, now) }) : translate('McpDown'),
        detail: reason ? `${reason} · ${seen}` : seen,
      };
    }
    case 'waiting':
      return { kind, icon, label: translate('McpWaiting'), detail: null };
    default:
      return {
        kind,
        icon,
        label: translate('McpNotChecked'),
        detail: translate(server.transport === 'stdio' ? 'McpNotCheckedLocal' : 'McpNotCheckedOff'),
      };
  }
}
