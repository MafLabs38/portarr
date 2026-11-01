import AppIcon from 'Components/AppIcon/AppIcon';
import * as icons from 'Helpers/Props/icons';
import type App from 'typings/App';
import type { CiRun, Repository } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './RepositoryRow.module.css';
import SeverityChips from './SeverityChips';

interface RepositoryRowProps {
  repository: Repository;
  apps: Map<string, App>;
}

function ciLabel(ci: CiRun): { text: string; className: string | undefined } {
  if (ci.conclusion === null) {
    return { text: translate('CiRunning'), className: styles.ciRunning };
  }
  if (ci.failed) {
    return { text: translate('CiFailed'), className: styles.ciFailed };
  }
  return ci.conclusion === 'success'
    ? { text: translate('CiPassed'), className: styles.ciPassed }
    : { text: translate('CiOther', { conclusion: ci.conclusion }), className: styles.ciRunning };
}

/**
 * One repository the operator makes, on one line: what it is (name, visibility, language, what runs from it),
 * its security alerts (Dependabot, code scanning, secrets), then its health as a project (CI of the default
 * branch, latest release, Dependabot pull requests waiting, last push).
 */
export default function RepositoryRow({ repository, apps }: Readonly<RepositoryRowProps>) {
  const linked = repository.appIds.map((id) => apps.get(id)).filter((app): app is App => app !== undefined);
  const ci = repository.ci ? ciLabel(repository.ci) : null;
  const base = repository.url;

  return (
    <li aria-label={repository.fullName} className={styles.row}>
      <div className={styles.what}>
        <a className={styles.name} href={base} rel="noreferrer" target="_blank">
          <icons.CODE_REPOSITORY aria-hidden="true" size={14} strokeWidth={2.25} />
          <span className={styles.owner}>{repository.owner}/</span>
          {repository.name}
        </a>
        {repository.visibility === 'public' ? null : (
          <span className={styles.meta} title={translate('Visibility_private')}>
            <icons.PRIVATE aria-label={translate('Visibility_private')} size={12} strokeWidth={2.5} />
          </span>
        )}
        {repository.language ? <span className={styles.meta}>{repository.language}</span> : null}
        {linked.map((app) => (
          <span className={styles.app} key={app.id}>
            <AppIcon icon={app.icon} name={app.name} size={16} />
            {app.name}
          </span>
        ))}
        {linked.length === 0 && repository.images.length > 0 ? (
          <span className={styles.app}>{translate('RunsAs', { images: repository.images.join(', ') })}</span>
        ) : null}
      </div>

      <div className={styles.security}>
        <SeverityChips
          label={translate('Dependabot')}
          scan={repository.dependabot}
          url={`${base}/security/dependabot`}
        />
        <SeverityChips
          label={translate('CodeScanning')}
          scan={repository.codeScanning}
          url={`${base}/security/code-scanning`}
        />
        {repository.secretScanning.state === 'off' && repository.visibility !== 'public' ? null : (
          <SeverityChips
            label={translate('Secrets')}
            scan={repository.secretScanning}
            url={`${base}/security/secret-scanning`}
          />
        )}
      </div>

      <div className={styles.project}>
        {ci && repository.ci ? (
          <a
            className={`${styles.ci} ${ci.className ?? ''}`}
            href={repository.ci.url ?? `${base}/actions`}
            rel="noreferrer"
            target="_blank"
            title={repository.ci.workflow ?? undefined}
          >
            <icons.CI aria-hidden="true" size={12} strokeWidth={2.5} />
            {ci.text}
          </a>
        ) : (
          <span className={styles.meta}>{translate('NoCi')}</span>
        )}
        {repository.release ? (
          <a
            className={styles.meta}
            href={repository.release.url ?? `${base}/tags`}
            rel="noreferrer"
            target="_blank"
            title={
              repository.release.publishedAt
                ? translate('ReleasedAge', { age: formatAge(repository.release.publishedAt) })
                : undefined
            }
          >
            <icons.RELEASE aria-hidden="true" size={12} strokeWidth={2.5} />
            {repository.release.tag}
          </a>
        ) : null}
        {repository.dependabotPullRequests ? (
          <a className={styles.pulls} href={`${base}/pulls/app%2Fdependabot`} rel="noreferrer" target="_blank">
            <icons.PULL_REQUEST aria-hidden="true" size={12} strokeWidth={2.5} />
            {translate('DependabotPullRequests', { count: repository.dependabotPullRequests })}
          </a>
        ) : null}
        <span className={styles.last}>
          {repository.pushedAt ? translate('PushedAge', { age: formatAge(repository.pushedAt) }) : null}
        </span>
      </div>
    </li>
  );
}
