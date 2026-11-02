import Alert from 'Components/Alert/Alert';
import translate from 'Utilities/String/translate';
import { useEnvironments } from 'Workloads/useWorkloads';

import styles from './UnreachableNotice.module.css';

const troubleshooting =
  'https://github.com/MafLabs38/portarr/blob/main/docs/troubleshooting.md#a-machine-is-unreachable';

/** The cause in words a person who does not run the network can act on. */
function hint(error: string | null): string {
  if (error === 'timeout') {
    return translate('ReasonHintTimeout');
  }
  if (error === 'unreachable') {
    return translate('ReasonHintUnreachable');
  }
  if (error === 'http-401' || error === 'http-403') {
    return translate('ReasonHintRefused');
  }
  return translate('ReasonHintOther');
}

/**
 * Shown on every page while Portarr cannot read an environment: what it displays for it (unknown apps, backups that cannot be
 * checked) is not a failure of what runs there, and the usual causes are a network matter, not the application.
 */
export default function UnreachableNotice() {
  const { data } = useEnvironments();
  const unreachable = (data ?? []).filter((environment) => environment.connection === 'unreachable');
  if (unreachable.length === 0) {
    return null;
  }

  const names = unreachable.map((environment) => environment.name).join(', ');
  return (
    <div className={styles.notice}>
      <Alert kind="warning">
        <strong>
          {unreachable.length === 1
            ? translate('UnreachableNoticeOne', { names })
            : translate('UnreachableNoticeMany', { count: unreachable.length, names })}
        </strong>
        <p>{translate('UnreachableNoticeBody')}</p>
        <ul>
          {unreachable.map((environment) => (
            <li key={environment.id}>
              {environment.name}: {hint(environment.error)}
            </li>
          ))}
        </ul>
        <p>
          <a href={troubleshooting} rel="noreferrer" target="_blank">
            {translate('UnreachableNoticeHelp')}
          </a>
        </p>
      </Alert>
    </div>
  );
}
