import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import table from 'Components/Table/Table.module.css';
import type { NotificationCategory, NotificationLog, NotificationLogEntry } from 'typings/Notifications';
import { formatDateTime } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

import styles from './Notifications.module.css';

interface MailLogProps {
  log: NotificationLog | undefined;
  categories: NotificationCategory[];
  failed: boolean;
}

/** The mails Portarr tried to send, newest first, delivered or not (the server keeps the latest 50). */
export default function MailLog({ log, failed, categories }: Readonly<MailLogProps>) {
  const categoryOf = (entry: NotificationLogEntry) => {
    if (entry.kind === 'test') {
      return translate('NotificationsTestMail');
    }
    return (
      categories.find(({ id }) => id === entry.category)?.title ??
      entry.category ??
      translate('NotificationsUnknownCategory')
    );
  };

  if (failed) {
    return <Alert kind="danger">{translate('ErrorLoadingNotificationLog')}</Alert>;
  }

  return (
    <Card title={translate('NotificationsRecentMails')}>
      {log && log.entries.length === 0 ? <p className={styles.empty}>{translate('NotificationsNoMails')}</p> : null}
      {log && log.entries.length > 0 ? (
        <div className={table.wrapper}>
          <table aria-label={translate('NotificationsRecentMails')} className={table.table}>
            <thead>
              <tr>
                <th>{translate('When')}</th>
                <th>{translate('NotificationsCategory')}</th>
                <th>{translate('NotificationsLevel')}</th>
                <th>{translate('NotificationsReminder')}</th>
                <th>{translate('NotificationsResult')}</th>
              </tr>
            </thead>
            <tbody>
              {log.entries.map((entry) => (
                <tr key={entry.id}>
                  <td className={table.muted}>{formatDateTime(entry.sentAt)}</td>
                  <td>{categoryOf(entry)}</td>
                  <td>{translate(`NotificationsLevel_${entry.level}`)}</td>
                  <td className={table.muted}>
                    {entry.reminder === 0
                      ? translate('NotificationsFirstMail')
                      : translate('NotificationsReminderNumber', { number: entry.reminder })}
                  </td>
                  <td>
                    <span className={entry.delivered ? table.badge : `${table.badge} ${table.warning}`}>
                      {translate(entry.delivered ? 'NotificationsDelivered' : 'NotificationsNotDelivered')}
                    </span>
                    {entry.error ? <span className={table.muted}> {entry.error}</span> : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      {log ? null : <p className={styles.empty}>{translate('Loading')}</p>}
    </Card>
  );
}
