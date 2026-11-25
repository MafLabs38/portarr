import type { Alert, AlertScan, Repository } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import styles from './RepositoryAlerts.module.css';

interface RepositoryAlertsProps {
  repository: Repository;
}

/** The scans of a repository that describe alerts, in the order the row shows them. */
function scansOf(repository: Repository): AlertScan[] {
  return [repository.dependabot, repository.codeScanning, repository.secretScanning];
}

export function hasAlertDetails(repository: Repository): boolean {
  return scansOf(repository).some((scan) => scan.alerts.length > 0);
}

function severityName(alert: Alert): string {
  if (alert.severity) {
    return translate(`SeverityName_${alert.severity}`);
  }
  return translate(alert.source === 'secret-scanning' ? 'SeverityName_secret' : 'SeverityName_quality');
}

/** What is wrong and what mends it: the package and its range, the fixed version (or the lack of one), the flaw's names. */
function facts(alert: Alert): string[] {
  const parts: string[] = [];
  if (alert.package) {
    parts.push(alert.ecosystem ? `${alert.package} (${alert.ecosystem})` : alert.package);
  }
  if (alert.vulnerableRange) {
    parts.push(translate('AlertVulnerable', { range: alert.vulnerableRange }));
  }
  if (alert.source === 'dependabot') {
    parts.push(
      alert.fixedVersion ? translate('AlertFixedIn', { version: alert.fixedVersion }) : translate('AlertNoFix'),
    );
  }
  const names = [alert.cveId, alert.advisoryId].filter((name): name is string => name !== null);
  if (names.length > 0) {
    parts.push(names.join(' · '));
  }
  if (alert.location) {
    parts.push(alert.location);
  }
  return parts;
}

/**
 * The worst open alerts of a repository, one line each, read without leaving the page: how bad, what it is,
 * which package and range, which version mends it, which advisory names it, which file it points at. Every alert
 * links to its page on the code host. When a feature has more alerts than are described, the line says how many.
 */
export default function RepositoryAlerts({ repository }: Readonly<RepositoryAlertsProps>) {
  const alerts = scansOf(repository).flatMap((scan) => scan.alerts);
  const hidden = scansOf(repository).reduce((sum, scan) => sum + Math.max(0, scan.total - scan.alerts.length), 0);

  return (
    <div className={styles.alerts}>
      <ul aria-label={translate('AlertsOf', { repository: repository.fullName })} className={styles.list}>
        {alerts.map((alert) => (
          <li className={styles.alert} key={`${alert.source}-${alert.id}`}>
            <span className={`${styles.severity} ${styles[alert.severity ?? 'other']}`}>{severityName(alert)}</span>
            {alert.url ? (
              <a className={styles.title} href={alert.url} rel="noreferrer" target="_blank">
                {alert.title}
              </a>
            ) : (
              <span className={styles.title}>{alert.title}</span>
            )}
            <span className={styles.facts}>{facts(alert).join(' · ')}</span>
          </li>
        ))}
      </ul>
      {hidden > 0 ? (
        <p className={styles.more}>
          {translate('AlertShownOf', { shown: alerts.length, total: alerts.length + hidden })}
        </p>
      ) : null}
    </div>
  );
}
