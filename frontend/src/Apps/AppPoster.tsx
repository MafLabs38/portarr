import AppIcon from 'Components/AppIcon/AppIcon';
import StatusLabel from 'Components/Status/StatusLabel';
import * as icons from 'Helpers/Props/icons';
import type App from 'typings/App';
import translate from 'Utilities/String/translate';

import styles from './AppPoster.module.css';
import { pickUrl, resolveScope, useUrlModeStore } from './urlMode';
import WorkloadSummary from './WorkloadSummary';

interface AppPosterProps {
  app: App;
}

/**
 * A launcher tile (F-CAT-01): a web app opens its URL for the current network (D-46); desktop apps
 * and services are not launchable. The frame color is the app status (D-45).
 */
export default function AppPoster({ app }: AppPosterProps) {
  const mode = useUrlModeStore((state) => state.mode);
  const mainUrl = pickUrl(app.urls, resolveScope(mode, window.location.hostname));
  const otherUrls = app.urls.filter((url) => url !== mainUrl);
  const TypeIcon = app.type === 'desktop' ? icons.DESKTOP : icons.SERVICE;

  const body = (
    <>
      <AppIcon icon={app.icon} name={app.name} size={56} />
      <span className={styles.name}>{app.name}</span>
    </>
  );

  return (
    <article aria-label={app.name} className={`${styles.poster} ${styles[app.status.status.toLowerCase()]}`}>
      {/* The frame color repeats the status; the label says it (DESIGN.md §5). */}
      <span className={styles.status}>
        <StatusLabel compact status={app.status} />
      </span>
      {app.type === 'web' && mainUrl ? (
        <a className={styles.launch} href={mainUrl.href} rel="noopener noreferrer" target="_blank" title={mainUrl.href}>
          {body}
        </a>
      ) : (
        <div className={styles.launch}>
          {body}
          {app.type !== 'web' ? (
            <span className={styles.type}>
              <TypeIcon aria-hidden="true" size={12} />
              {translate(app.type === 'desktop' ? 'AppTypeDesktop' : 'AppTypeService')}
            </span>
          ) : null}
        </div>
      )}
      <WorkloadSummary workloads={app.workloads} />
      {otherUrls.length > 0 ? (
        <div className={styles.links}>
          {otherUrls.map((url) => (
            <a href={url.href} key={url.href} rel="noopener noreferrer" target="_blank">
              {url.label}
            </a>
          ))}
        </div>
      ) : null}
    </article>
  );
}
