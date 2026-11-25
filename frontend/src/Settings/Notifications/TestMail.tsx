import { useState } from 'react';

import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import type { NotificationTestResult } from 'typings/Notifications';
import translate from 'Utilities/String/translate';

import styles from './Notifications.module.css';
import { useSendTestMail } from './useNotifications';

/** What the last attempt said: the server's answer, or the call itself failing. */
type Outcome = NotificationTestResult | 'failed';

/** Sends a test mail to the saved recipient, and tells at once whether it left (the log below shows it too). */
export default function TestMail() {
  const send = useSendTestMail();
  const [outcome, setOutcome] = useState<Outcome | null>(null);

  const onClick = async () => {
    setOutcome(null);
    try {
      setOutcome(await send.mutateAsync(undefined));
    } catch {
      setOutcome('failed');
    }
  };

  return (
    <Card title={translate('NotificationsTest')}>
      <p className={styles.help}>{translate('NotificationsTestHelp')}</p>
      <div className={form.actions}>
        <button className={form.secondary} disabled={send.isPending} onClick={onClick} type="button">
          {translate('NotificationsSendTest')}
        </button>
        {outcome && outcome !== 'failed' && outcome.ok ? (
          <output className={`${styles.testResult} ${styles.ok}`}>{translate('NotificationsTestSent')}</output>
        ) : null}
        {outcome && outcome !== 'failed' && !outcome.ok ? (
          <p className={`${styles.testResult} ${styles.failed}`} role="alert">
            {translate('NotificationsTestNotSent', {
              reason: outcome.error ?? translate('NotificationsUnknownReason'),
            })}
          </p>
        ) : null}
        {outcome === 'failed' ? (
          <p className={`${styles.testResult} ${styles.failed}`} role="alert">
            {translate('NotificationsTestCallFailed')}
          </p>
        ) : null}
      </div>
    </Card>
  );
}
