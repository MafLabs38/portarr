import type { ImageFinding, ImageVulnerabilities, KnownExploited } from 'typings/Maintenance';
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
  return url?.startsWith('https://') === true;
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

/** An EPSS probability as a short percentage: two significant figures under 1 %, a whole number above. */
function formatScore(score: number): string {
  const percent = score * 100;
  return percent >= 1 ? `${Math.round(percent)} %` : `${percent.toPrecision(2)} %`;
}

/** Known to be exploited in the wild (CISA KEV, D-133), with the date it was listed and the ransomware flag. */
function ExploitedBadge({ exploited }: Readonly<{ exploited: KnownExploited }>) {
  return (
    <span className={styles.exploited} title={translate('FindingExploitedSince', { date: exploited.dateAdded })}>
      {translate(exploited.knownRansomwareUse ? 'FindingExploitedRansomware' : 'FindingExploited')}
    </span>
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
              {finding.exploited ? <ExploitedBadge exploited={finding.exploited} /> : null}
              <FindingId finding={finding} />
              {finding.epss ? (
                <span
                  className={styles.epss}
                  title={translate('FindingEpssTitle', { percentile: Math.round(finding.epss.percentile * 100) })}
                >
                  {translate('FindingEpss', { score: formatScore(finding.epss.score) })}
                </span>
              ) : null}
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
