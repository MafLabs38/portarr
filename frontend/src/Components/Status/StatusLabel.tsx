import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type Status from 'typings/Status';
import type { HealthStatus } from 'typings/Status';
import translate from 'Utilities/String/translate';

import styles from './StatusLabel.module.css';

// Mandatory mapping (DESIGN.md §5): a status is never conveyed by color alone.
const mapping: Record<HealthStatus, { kind: kinds.Kind; icon: LucideIcon }> = {
  Healthy: { kind: kinds.SUCCESS, icon: icons.HEALTHY },
  Degraded: { kind: kinds.WARNING, icon: icons.DEGRADED },
  Down: { kind: kinds.DANGER, icon: icons.DOWN },
  Unknown: { kind: kinds.DISABLED, icon: icons.UNKNOWN },
  Maintenance: { kind: kinds.INFO, icon: icons.MAINTENANCE },
  Paused: { kind: kinds.INVERSE, icon: icons.PAUSED },
};

interface StatusLabelProps {
  status: Status;
  /** Shows the reason next to the status instead of only in the tooltip. */
  showReason?: boolean;
  /** Icon only, the status text stays available to assistive technologies. */
  compact?: boolean;
}

export default function StatusLabel({ status, showReason = false, compact = false }: StatusLabelProps) {
  const { kind, icon: Icon } = mapping[status.status];

  return (
    <span className={`${compact ? styles.compact : styles.label} ${styles[kind]}`} title={status.reason}>
      <Icon aria-hidden="true" className={styles.icon} size={14} strokeWidth={2.5} />
      <span className={compact ? styles.visuallyHidden : undefined}>{translate(`Status${status.status}`)}</span>
      {showReason ? <span className={styles.reason}>· {status.reason}</span> : null}
    </span>
  );
}
