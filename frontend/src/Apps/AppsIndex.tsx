import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import type App from 'typings/App';
import translate from 'Utilities/String/translate';

import AppPoster from './AppPoster';
import styles from './AppsIndex.module.css';
import UrlModeSwitch from './UrlModeSwitch';
import { useApps, useCatalogStatus, useStacks } from './useApps';

/** Apps grouped by stack, in catalog order; apps without a stack come last. */
export default function AppsIndex() {
  const apps = useApps();
  const stacks = useStacks();
  const catalog = useCatalogStatus();

  const byId = new Map((apps.data ?? []).map((app) => [app.id, app]));
  const grouped = new Set<string>();
  const sections = (stacks.data ?? []).map((stack) => {
    const members = stack.members.map((member) => byId.get(member.appId)).filter((app): app is App => Boolean(app));
    members.forEach((app) => grouped.add(app.id));
    return { stack, members };
  });
  const ungrouped = (apps.data ?? []).filter((app) => !grouped.has(app.id));

  return (
    <PageContent title={translate('Apps')}>
      <PageContentBody>
        <div className={styles.heading}>
          <PageHeading scope={translate('Launcher')} title={translate('Apps')} />
          <UrlModeSwitch />
        </div>

        {catalog.data && catalog.data.errors.length > 0 ? (
          <Alert kind="warning">
            <strong>{translate('CatalogErrors')}</strong>
            <ul className={styles.errors}>
              {catalog.data.errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          </Alert>
        ) : null}

        {apps.isError ? <Alert kind="danger">{translate('ErrorLoadingApps')}</Alert> : null}

        {apps.data && apps.data.length === 0 ? (
          <div className={styles.empty}>
            <p className={styles.ahoy}>{translate('AppsEmptyTitle')}</p>
            <p>{translate('AppsEmptyHint', { path: catalog.data?.filePath ?? 'catalog.yaml' })}</p>
          </div>
        ) : null}

        {sections
          .filter((section) => section.members.length > 0)
          .map(({ stack, members }) => (
            <section aria-label={stack.name} className={styles.section} key={stack.id}>
              <header className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>{stack.name}</h2>
                <StatusLabel showReason={stack.status.status !== 'Healthy'} status={stack.status} />
              </header>
              <div className={styles.grid}>
                {members.map((app) => (
                  <AppPoster app={app} key={app.id} />
                ))}
              </div>
            </section>
          ))}

        {ungrouped.length > 0 ? (
          <section aria-label={translate('OtherApps')} className={styles.section}>
            {sections.length > 0 ? (
              <header className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>{translate('OtherApps')}</h2>
              </header>
            ) : null}
            <div className={styles.grid}>
              {ungrouped.map((app) => (
                <AppPoster app={app} key={app.id} />
              ))}
            </div>
          </section>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
