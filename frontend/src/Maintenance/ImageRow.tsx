import AppIcon from 'Components/AppIcon/AppIcon';
import * as icons from 'Helpers/Props/icons';
import type App from 'typings/App';
import type { DeployedImage } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './ImageRow.module.css';
import ImageStatusLabel from './ImageStatusLabel';

interface ImageRowProps {
  image: DeployedImage;
  apps: Map<string, App>;
}

const knownReasons = new Set([
  'floating',
  'image-id',
  'invalid-response',
  'not-found',
  'pinned',
  'rate-limited',
  'refused',
  'timeout',
  'unreachable',
]);

/** Shown names: the apps of the workloads, else the workloads themselves. */
function usersOf(image: DeployedImage, apps: Map<string, App>) {
  const linked = new Map<string, App>();
  const others: string[] = [];
  for (const user of image.users) {
    const found = user.appIds.map((id) => apps.get(id)).filter((app): app is App => app !== undefined);
    found.forEach((app) => linked.set(app.id, app));
    if (found.length === 0) {
      others.push(user.name);
    }
  }
  // The same container name in several environments is shown once.
  return { linked: [...linked.values()], others: [...new Set(others)] };
}

/**
 * One running image on one line: its verdict, the reference, the version it runs and the newest one of the
 * same kind (with how old that release is), then what runs it and where. A verdict Portarr cannot reach says
 * why, rather than guessing (D-66).
 */
export default function ImageRow({ image, apps }: Readonly<ImageRowProps>) {
  const { linked, others } = usersOf(image, apps);
  const environments = [...new Set(image.users.map((user) => user.environmentName))].join(', ');
  const isImageId = image.reason === 'image-id';
  const shownOthers = others.slice(0, 3);
  const reason = image.reason && knownReasons.has(image.reason) ? image.reason : 'other';

  return (
    <li aria-label={isImageId ? (image.users[0]?.name ?? image.reference) : image.reference} className={styles.row}>
      <ImageStatusLabel image={image} />

      <div className={styles.what}>
        <span className={styles.reference} title={image.reference}>
          <icons.IMAGE aria-hidden="true" size={14} strokeWidth={2.25} />
          {image.reference}
        </span>
        <span className={styles.versions}>
          {image.current && image.current !== image.tag ? (
            <span className={styles.current} title={image.fromLabel ? translate('FromLabelHint') : undefined}>
              {image.current}
              {image.fromLabel ? <sup aria-hidden="true">*</sup> : null}
            </span>
          ) : null}
          {image.latest && image.latest !== image.current ? (
            <>
              <span aria-hidden="true">→</span>
              <span className={styles.latest}>{image.latest}</span>
            </>
          ) : null}
          {image.latestAt && image.status !== 'up-to-date' ? (
            <span className={styles.age}>{translate('ReleasedAge', { age: formatAge(image.latestAt) })}</span>
          ) : null}
          {image.reason ? <span className={styles.reason}>{translate(`ImageReason_${reason}`)}</span> : null}
        </span>
      </div>

      <div className={styles.users}>
        {linked.map((app) => (
          <span className={styles.app} key={app.id}>
            <AppIcon icon={app.icon} name={app.name} size={16} />
            {app.name}
          </span>
        ))}
        {shownOthers.map((name) => (
          <span className={styles.workload} key={name}>
            {name}
          </span>
        ))}
        {others.length > shownOthers.length ? (
          <span className={styles.workload}>+{others.length - shownOthers.length}</span>
        ) : null}
        <span className={styles.environment}>{environments}</span>
      </div>
    </li>
  );
}
