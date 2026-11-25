import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import SettingsNav from 'Settings/SettingsNav';
import translate from 'Utilities/String/translate';

import ConnectionCard from './ConnectionCard';
import MailLog from './MailLog';
import styles from './Notifications.module.css';
import NotificationSettingsForm from './NotificationSettingsForm';
import TestMail from './TestMail';
import { useNotificationLog, useNotificationSettings } from './useNotifications';

/**
 * Settings, Notifications (D-129, F-NOT-01), behind `settings.manage`: whether Portarr writes to the operator, to whom
 * and, per category, from which level, how often and how many, where the mail goes out (read-only: a setting of the deployment), a test mail, and the latest mails.
 */
export default function NotificationsPage() {
  const settings = useNotificationSettings();
  const log = useNotificationLog();

  return (
    <PageContent title={translate('Notifications')}>
      <PageContentBody>
        <PageHeading scope={translate('Settings')} title={translate('Notifications')} />
        <SettingsNav />
        {settings.isError ? <Alert kind="danger">{translate('ErrorLoadingNotifications')}</Alert> : null}
        {settings.data?.problem ? (
          <Alert kind="warning">{translate('NotificationsProblem', { problem: settings.data.problem })}</Alert>
        ) : null}
        {settings.data?.secretProblem ? (
          <Alert kind="warning">
            {translate('NotificationsSecretProblem', { problem: settings.data.secretProblem })}
          </Alert>
        ) : null}
        {settings.isPending ? <p>{translate('Loading')}</p> : null}
        {settings.data ? (
          <div className={styles.stack}>
            <NotificationSettingsForm settings={settings.data} />
            <ConnectionCard connection={settings.data.connection} />
            <TestMail />
            <MailLog categories={settings.data.categories} failed={log.isError} log={log.data} />
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
