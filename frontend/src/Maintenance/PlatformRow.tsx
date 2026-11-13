import * as icons from 'Helpers/Props/icons';
import type { Platform } from 'typings/Maintenance';
import { formatDay } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

import styles from './PlatformRow.module.css';
import PlatformStatusLabel from './PlatformStatusLabel';

/**
 * One platform of one machine on one line (D-119): its verdict, what it is and the version it runs, the newer versions that
 * exist (a patch of the same line, then the next line: one line at a time), when its support stops, and where it runs.
 * A support date nobody announced is said to be unknown, never taken for an endless support.
 */
export default function PlatformRow({ platform }: Readonly<{ platform: Platform }>) {
  const support = (() => {
    if (!platform.endOfLife) {
      return translate('PlatformSupportUnknown');
    }
    return translate(platform.level === 'danger' ? 'PlatformSupportEnded' : 'PlatformSupportEnds', {
      date: formatDay(platform.endOfLife),
    });
  })();

  return (
    <li aria-label={`${platform.machine} ${platform.name}`} className={styles.row}>
      <PlatformStatusLabel platform={platform} />

      <div className={styles.what}>
        <span className={styles.name}>
          <icons.NODE aria-hidden="true" size={14} strokeWidth={2.25} />
          {platform.name}
          {platform.name.includes(platform.version) ? null : <span className={styles.version}>{platform.version}</span>}
        </span>
        <span className={styles.details}>
          {platform.newestPatchOfMinor ? (
            <span className={styles.newer}>{translate('PlatformPatch', { version: platform.newestPatchOfMinor })}</span>
          ) : null}
          {platform.nextMinor ? (
            <span className={styles.newer}>{translate('PlatformNextMinor', { version: platform.nextMinor })}</span>
          ) : null}
          <span>{support}</span>
        </span>
      </div>

      <div className={styles.where}>
        <span className={styles.machine}>{platform.machine}</span>
        <span className={styles.environment}>{platform.environmentName}</span>
      </div>
    </li>
  );
}
