import type { ThreatIntel } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './ImageScannerStatus.module.css';
import scanReason from './scanReason';

/**
 * One line about the exploit feeds (D-133): when Portarr last read CISA's known exploited catalog and EPSS and, when the
 * last read failed, why (the previous data stays). Nothing while they are off or no scanner gives them findings to mark.
 */
export default function ThreatIntelStatus({ threatIntel }: Readonly<{ threatIntel: ThreatIntel | undefined }>) {
  if (!threatIntel?.configured) {
    return null;
  }

  return (
    <p className={styles.line}>
      <span>
        {translate('ThreatIntelStatus')}:{' '}
        {threatIntel.readAt
          ? translate('ThreatIntelRead', { age: formatAge(threatIntel.readAt) })
          : translate('ThreatIntelNotReadYet')}
      </span>
      {threatIntel.error ? (
        <span className={styles.error}>
          {translate('ThreatIntelUnreadable', { reason: scanReason(threatIntel.error) })}
        </span>
      ) : null}
    </p>
  );
}
