import type { ImageFinding, ImageVulnerabilities } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './ImageFindings.module.css';

interface ImageFindingsProps {
  id: string;
  image: string;
  vulnerabilities: ImageVulnerabilities;
}

/** Only an https address becomes a link: the advisory database is third-party data. */
function isSecureAddress(url: string | null): url is string {
  return url !== null && url.startsWith('https://');
}

function FindingId({ finding }: Readonly<{ finding: ImageFinding }>) {
  return isSecureAddress(finding.url) ? (
    <a className={styles.id} href={finding.url} rel="noreferrer" target="_blank">
      {finding.id}
    </a>
  ) : (
    <span className={styles.id}>{finding.id}</span>
  );
}

/**
 * The worst vulnerabilities of one image (D-131): severity, advisory id, package, the version it runs and the one that
 * fixes it, and the advisory's title cut to one line. Every text comes from a vulnerability database and is shown as
 * plain text, never as markup.
 */
export default function ImageFindings({ id, image, vulnerabilities }: Readonly<ImageFindingsProps>) {
  const { counts, fixable, findings, scannedAt } = vulnerabilities;
  const severe = counts.critical + counts.high;

  return (
    <div className={styles.panel} id={id}>
      <p className={styles.summary}>
        {scannedAt ? <span>{translate('ScannedAge', { age: formatAge(scannedAt) })}</span> : null}
        <span>{translate('VulnerabilitiesFixable', { count: fixable.total })}</span>
        {findings.length > 0 && findings.length < severe ? (
          <span>{translate('FindingsShown', { shown: findings.length, total: severe })}</span>
        ) : null}
      </p>
      {findings.length === 0 ? (
        <p className={styles.empty}>{translate('NoFindingsShown')}</p>
      ) : (
        <ul aria-label={translate('VulnerabilitiesOf', { image })} className={styles.list}>
          {findings.map((finding) => (
            <li className={styles.finding} key={`${finding.id}|${finding.package}|${finding.installedVersion}`}>
              <span className={`${styles.severity} ${styles[finding.severity ?? 'unrated']}`}>
                {translate(`FindingSeverity_${finding.severity ?? 'unrated'}`)}
              </span>
              <FindingId finding={finding} />
              <span className={styles.package}>{finding.package}</span>
              <span className={styles.versions}>
                {finding.installedVersion}
                <span aria-hidden="true"> → </span>
                {finding.fixedVersion ? (
                  <span className={styles.fixed}>{finding.fixedVersion}</span>
                ) : (
                  <span className={styles.noFix}>{translate('FindingNoFix')}</span>
                )}
              </span>
              {finding.title ? (
                <span className={styles.title} title={finding.title}>
                  {finding.title}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
