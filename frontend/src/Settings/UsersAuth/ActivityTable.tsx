import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import styles from 'Components/Table/Table.module.css';
import type { AuthActivity } from 'typings/Auth';
import { formatDateTime } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

interface ActivityTableProps {
  activity: AuthActivity[] | undefined;
  failed: boolean;
}

/** The latest events of the accounts (V7, D-96): sign-ins and failures, changes to users, roles and keys. Never a secret. */
export default function ActivityTable({ activity, failed }: Readonly<ActivityTableProps>) {
  if (failed) {
    return <Alert kind="danger">{translate('ErrorLoadingActivity')}</Alert>;
  }

  return (
    <Card title={translate('Activity')}>
      <div className={styles.wrapper}>
        <table aria-label={translate('Activity')} className={styles.table}>
          <thead>
            <tr>
              <th>{translate('When')}</th>
              <th>{translate('Event')}</th>
              <th>{translate('By')}</th>
              <th>{translate('Subject')}</th>
              <th>{translate('Detail')}</th>
              <th>{translate('Client')}</th>
            </tr>
          </thead>
          <tbody>
            {(activity ?? []).map((entry, index) => (
              // The log has no id: time and position identify a line.
              <tr key={`${entry.at}-${index}`}>
                <td className={styles.muted}>{formatDateTime(entry.at)}</td>
                <td>
                  <span
                    className={entry.event.endsWith('failure') ? `${styles.badge} ${styles.warning}` : styles.badge}
                  >
                    {entry.event}
                  </span>
                </td>
                <td>{entry.actor ?? ''}</td>
                <td>{entry.subject ?? ''}</td>
                <td className={styles.muted}>{entry.detail ?? ''}</td>
                <td className={`${styles.mono} ${styles.muted}`}>{entry.client ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
