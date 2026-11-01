import * as icons from 'Helpers/Props/icons';
import type { AlertScan } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import styles from './SeverityChips.module.css';

interface SeverityChipsProps {
  /** "Dependabot", "Code scanning", "Secrets". */
  label: string;
  scan: AlertScan;
  url?: string;
}

const severities = ['critical', 'high', 'medium', 'low', 'other'] as const;

/**
 * One security feature of a repository: its open alerts as chips colored by severity (and named, never by
 * color alone), "off" in a dashed chip when the feature is disabled, "?" when the token cannot read it.
 */
export default function SeverityChips({ label, scan, url }: Readonly<SeverityChipsProps>) {
  if (scan.state === 'off') {
    const description = translate('ScanningOffValue', { feature: label });
    return (
      <span aria-label={description} className={styles.off} role="img" title={description}>
        <icons.SCANNING_OFF aria-hidden="true" size={12} strokeWidth={2.5} />
        <span aria-hidden="true">{label}</span>
      </span>
    );
  }

  if (scan.state === 'unknown') {
    const reason = scan.reason ? translate(`Reason_${scan.reason}`) : translate('StatusUnknown');
    const description = translate('ScanningUnknownValue', { feature: label, reason });
    return (
      <span aria-label={description} className={styles.unknown} role="img" title={description}>
        <span aria-hidden="true">{label} ?</span>
      </span>
    );
  }

  const present = severities.filter((severity) => scan[severity] > 0);
  const parts = present.map((severity) => translate(`Severity_${severity}`, { count: scan[severity] }));
  const description = `${label}: ${parts.length > 0 ? parts.join(', ') : translate('NoOpenAlert')}`;
  const content = (
    <>
      <span className={styles.name}>{label}</span>
      {present.length === 0 ? (
        <span className={`${styles.chip} ${styles.clean}`}>
          <icons.HEALTHY aria-hidden="true" size={12} strokeWidth={2.5} />0
        </span>
      ) : (
        present.map((severity) => (
          <span className={`${styles.chip} ${styles[severity]}`} key={severity}>
            {translate(`Severity_${severity}`, { count: scan[severity] })}
          </span>
        ))
      )}
    </>
  );

  return url && present.length > 0 ? (
    // No aria-label: the link is named by what it shows (its feature and chips), as WCAG 2.5.3 asks.
    <a className={styles.group} href={url} rel="noreferrer" target="_blank" title={description}>
      {content}
    </a>
  ) : (
    <span aria-label={description} className={styles.group} role="img" title={description}>
      {content}
    </span>
  );
}
