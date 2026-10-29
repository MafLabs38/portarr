import { Link } from 'react-router-dom';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import translate from 'Utilities/String/translate';

import { useEnvironments, useWorkloads } from './useWorkloads';
import styles from './WorkloadsIndex.module.css';

/** Every discovered container, per environment, with the app it serves (F-INV-01). */
export default function WorkloadsIndex() {
  const workloads = useWorkloads();
  const environments = useEnvironments();

  return (
    <PageContent title={translate('Workloads')}>
      <PageContentBody>
        <PageHeading scope={translate('Infrastructure')} title={translate('Workloads')} />

        {workloads.isError ? <Alert kind="danger">{translate('ErrorLoadingWorkloads')}</Alert> : null}

        {environments.data && environments.data.length === 0 ? (
          <p className={styles.empty}>{translate('WorkloadsEmptyHint')}</p>
        ) : null}

        {(environments.data ?? []).map((environment) => {
          const rows = (workloads.data ?? []).filter((w) => w.environmentId === environment.id);

          return (
            <section aria-label={environment.name} className={styles.section} key={environment.id}>
              <header className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>{environment.name}</h2>
                <span className={styles.meta}>
                  {translate('ContainerCount', { count: environment.workloads })} ·{' '}
                  {translate(`Connection_${environment.connection}`)}
                </span>
              </header>

              {environment.connection === 'unreachable' ? (
                <Alert kind="warning">
                  {translate('EnvironmentUnreachable', {
                    reason: translate(`Reason_${environment.error ?? 'unreachable'}`),
                  })}
                </Alert>
              ) : null}
              {environment.connection === 'not-connected' ? (
                <p className={styles.muted}>{translate('EnvironmentNotConnected')}</p>
              ) : null}

              <div className={styles.tableWrapper}>
                <table className={styles.table}>
                  <thead>
                    <tr>
                      <th scope="col">{translate('Status')}</th>
                      <th scope="col">{translate('Name')}</th>
                      <th scope="col">{translate('Image')}</th>
                      <th scope="col">{translate('State')}</th>
                      <th scope="col">{translate('Project')}</th>
                      <th scope="col">{translate('App')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((workload) => (
                      <tr
                        className={workload.stale ? styles.stale : undefined}
                        data-stale={workload.stale}
                        key={workload.id}
                      >
                        <td>
                          <StatusLabel status={workload.status} />
                        </td>
                        <td>
                          <Link
                            className={styles.name}
                            to={`/workloads/${encodeURIComponent(environment.id)}/${encodeURIComponent(workload.name)}`}
                          >
                            {workload.name}
                          </Link>
                        </td>
                        <td className={styles.mono}>{workload.image}</td>
                        <td className={styles.muted}>{workload.statusText}</td>
                        <td className={styles.muted}>{workload.composeProject ?? '—'}</td>
                        <td>{workload.appIds.length > 0 ? workload.appIds.join(', ') : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          );
        })}
      </PageContentBody>
    </PageContent>
  );
}
