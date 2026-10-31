import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import type { CopyTarget, DataCopy } from 'typings/Data';
import translate from 'Utilities/String/translate';

import styles from './CopyCell.module.css';
import CoverageLabel from './CoverageLabel';
import { describeLastSuccess, describeReason, describeSource } from './describeCopy';

interface CopyCellProps {
  target: CopyTarget;
  /** Null: the catalog does not expect this copy. */
  copy: DataCopy | null;
}

const targetIcons: Record<CopyTarget, LucideIcon> = {
  local: icons.LOCAL_COPY,
  offsite: icons.OFFSITE_COPY,
};

/**
 * One expected copy of an asset (local on the array, or off-site in a bucket): its state, when it last
 * succeeded and the jobs behind it, each with the reason when the tool could not prove it. A copy the catalog
 * does not expect is shown dashed, so "no off-site copy wanted" is visible, never silent.
 */
export default function CopyCell({ target, copy }: Readonly<CopyCellProps>) {
  const Icon = targetIcons[target];
  const label = translate(target === 'local' ? 'LocalCopy' : 'OffsiteCopy');

  if (copy === null) {
    return (
      <div aria-label={`${label}: ${translate('NotFollowed')}`} className={styles.absent} role="group">
        <span className={styles.head}>
          <Icon aria-hidden="true" size={13} strokeWidth={2.5} />
          {label}
        </span>
        <span className={styles.meta}>{translate('NotFollowed')}</span>
      </div>
    );
  }

  return (
    <div aria-label={label} className={styles.cell} role="group">
      <span className={styles.head}>
        <Icon aria-hidden="true" size={13} strokeWidth={2.5} />
        {label}
        <CoverageLabel title={copy.reason ? describeReason(copy.reason) : undefined} value={copy.state} />
      </span>
      <span className={copy.state === 'failed' ? styles.failed : styles.meta}>{describeLastSuccess(copy)}</span>
      <ul className={styles.sources}>
        {copy.manual ? <li>{translate('MadeByHand')}</li> : null}
        {copy.sources.map((source) => (
          <li key={`${source.tool}@${source.environmentId}/${source.reference}`}>
            {describeSource(source)}
            {source.reason ? <span className={styles.reason}> · {describeReason(source.reason)}</span> : null}
          </li>
        ))}
        {copy.sources.length === 0 && !copy.manual ? (
          <li className={styles.reason}>{describeReason('no-source')}</li>
        ) : null}
      </ul>
    </div>
  );
}
