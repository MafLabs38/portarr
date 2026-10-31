import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { CopyState } from 'typings/Data';
import translate from 'Utilities/String/translate';

import styles from './CoverageLabel.module.css';

// A state is never conveyed by color alone (DESIGN.md §5): icon plus text.
// Waiting (info) is pending, not a problem; not set up (primary) is a configuration to fix; unknown (disabled)
// is a tool that could not be read. The three used to look the same.
const mapping: Record<CopyState, { kind: kinds.Kind; icon: LucideIcon }> = {
  protected: { kind: kinds.SUCCESS, icon: icons.PROTECTED },
  waiting: { kind: kinds.INFO, icon: icons.WAITING },
  late: { kind: kinds.WARNING, icon: icons.LATE },
  failed: { kind: kinds.DANGER, icon: icons.DOWN },
  missing: { kind: kinds.DANGER, icon: icons.NOT_COVERED },
  'not-set-up': { kind: kinds.PRIMARY, icon: icons.NOT_SET_UP },
  unknown: { kind: kinds.DISABLED, icon: icons.UNKNOWN },
};

interface CoverageLabelProps {
  value: CopyState;
  title?: string;
}

/** The state of a copy, or of an asset (the worst of its copies). */
export default function CoverageLabel({ value, title }: Readonly<CoverageLabelProps>) {
  const { kind, icon: Icon } = mapping[value];

  return (
    <span className={`${styles.label} ${styles[kind]}`} title={title}>
      <Icon aria-hidden="true" size={14} strokeWidth={2.5} />
      <span>{translate(`Coverage_${value}`)}</span>
    </span>
  );
}
