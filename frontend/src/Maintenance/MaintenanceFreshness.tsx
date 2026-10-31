import useNow from 'Helpers/Hooks/useNow';
import * as icons from 'Helpers/Props/icons';
import type { CodeSource, MaintenanceRefresh } from 'typings/Maintenance';
import formatAge, { formatUntil } from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './MaintenanceFreshness.module.css';
import useMaintenanceRefresh from './useMaintenanceRefresh';

interface MaintenanceFreshnessProps {
  refresh: MaintenanceRefresh;
  source: CodeSource;
}

/**
 * When the data was last read ("GitHub updated 12 min ago · next in 18 min", "Registries updated 3 h ago") and
 * the Refresh action that rescans now. The button spins until the new data is in; Portarr accepts one refresh a
 * minute and GitHub's rate-limit guard still applies, so the button waits instead of insisting.
 */
export default function MaintenanceFreshness({ refresh, source }: Readonly<MaintenanceFreshnessProps>) {
  const now = useNow(1_000);
  const request = useMaintenanceRefresh();
  const waitSeconds = refresh.availableAt ? Math.ceil((Date.parse(refresh.availableAt) - now) / 1_000) : 0;
  const busy = refresh.refreshing || request.isPending;
  const tooSoon = waitSeconds > 0;
  const hint = tooSoon ? translate('RefreshAvailableIn', { seconds: waitSeconds }) : translate('RefreshHint');

  return (
    <div className={styles.bar}>
      <p className={styles.status}>
        {source.configured ? (
          <span>
            {refresh.gitHubAt
              ? translate('GitHubUpdated', { age: formatAge(refresh.gitHubAt, now) })
              : translate('GitHubNotReadYet')}
            {refresh.gitHubNextAt && !busy ? (
              <>
                {' · '}
                {translate('NextIn', { time: formatUntil(refresh.gitHubNextAt, now) })}
              </>
            ) : null}
          </span>
        ) : null}
        <span>
          {refresh.registriesAt
            ? translate('RegistriesUpdated', { age: formatAge(refresh.registriesAt, now) })
            : translate('RegistriesNotReadYet')}
        </span>
      </p>
      <button
        aria-busy={busy}
        className={styles.button}
        disabled={busy || tooSoon}
        onClick={() => request.mutate()}
        title={hint}
        type="button"
      >
        <icons.REFRESH aria-hidden="true" className={busy ? styles.spinning : undefined} size={14} strokeWidth={2.5} />
        {busy ? translate('Refreshing') : translate('Refresh')}
      </button>
      {request.isError ? (
        <p className={styles.error} role="alert">
          {request.error.status === 429 ? translate('RefreshTooSoon') : translate('RefreshFailed')}
        </p>
      ) : null}
    </div>
  );
}
