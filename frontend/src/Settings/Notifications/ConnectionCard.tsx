import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import type { NotificationConnection } from 'typings/Notifications';
import translate from 'Utilities/String/translate';

import styles from './Notifications.module.css';

const notSet = () => translate('NotificationsNotSet');

/** Where the mail goes out (D-129). Read-only: it is a setting of the deployment, and the password is a file that is never shown. */
export default function ConnectionCard({ connection }: Readonly<{ connection: NotificationConnection }>) {
  return (
    <Card title={translate('NotificationsConnection')}>
      <p className={styles.help}>{translate('NotificationsConnectionHelp')}</p>
      <DescriptionList>
        <DescriptionListItem data={connection.provider} title={translate('NotificationsProvider')} />
        <DescriptionListItem data={connection.host || notSet()} monospace title={translate('NotificationsHost')} />
        <DescriptionListItem data={connection.port} monospace title={translate('NotificationsPort')} />
        <DescriptionListItem data={connection.userName || notSet()} monospace title={translate('NotificationsUser')} />
        <DescriptionListItem
          data={translate(connection.startTls ? 'NotificationsStartTlsOn' : 'NotificationsStartTlsOff')}
          title={translate('NotificationsStartTls')}
        />
        <DescriptionListItem
          data={translate(
            connection.passwordFileConfigured ? 'NotificationsPasswordSet' : 'NotificationsPasswordNotSet',
          )}
          title={translate('NotificationsPassword')}
        />
      </DescriptionList>
    </Card>
  );
}
