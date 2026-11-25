import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import translate from 'Utilities/String/translate';

import { type Draft, globalMaxPerDayRange, type Problems } from './notificationDraft';
import styles from './Notifications.module.css';
import NumberInput from './NumberInput';

interface MailSettingsCardProps {
  draft: Draft;
  problems: Problems;
  onChange: (patch: Partial<Draft>) => void;
}

/** The global settings: the master switch, whom the mail goes to and from, and the overall cap. */
export default function MailSettingsCard({ draft, problems, onChange }: Readonly<MailSettingsCardProps>) {
  const textField = (field: 'recipient' | 'from', label: string, help: string) => (
    <div className={form.field}>
      <label className={form.label} htmlFor={`notifications-${field}`}>
        {translate(label)}
      </label>
      <input
        aria-describedby={`notifications-${field}-help`}
        aria-invalid={problems.fields[field] ? true : undefined}
        autoCapitalize="off"
        className={form.input}
        id={`notifications-${field}`}
        onChange={(event) => onChange({ [field]: event.target.value })}
        spellCheck={false}
        type="email"
        value={draft[field]}
      />
      <span className={form.help} id={`notifications-${field}-help`}>
        {translate(help)}
      </span>
      {problems.fields[field]?.map((message) => (
        <p className={styles.fieldError} key={message}>
          {message}
        </p>
      ))}
    </div>
  );

  return (
    <Card title={translate('NotificationsMail')}>
      <p className={styles.help}>{translate('NotificationsHelpText')}</p>
      <div className={styles.fields}>
        <label className={styles.checkbox}>
          <input
            checked={draft.enabled}
            onChange={(event) => onChange({ enabled: event.target.checked })}
            type="checkbox"
          />
          {translate('NotificationsEnabled')}
        </label>
        {problems.fields.enabled?.map((message) => (
          <p className={styles.fieldError} key={message}>
            {message}
          </p>
        ))}
        <div className={styles.grid}>
          {textField('recipient', 'NotificationsRecipient', 'NotificationsRecipientHelp')}
          {textField('from', 'NotificationsFrom', 'NotificationsFromHelp')}
        </div>
        <div className={styles.grid}>
          <NumberInput
            errors={problems.fields.maxPerDay}
            help={translate('NotificationsMaxPerDayHelp', globalMaxPerDayRange)}
            id="notifications-maxPerDay"
            label={translate('NotificationsMaxPerDayAll')}
            max={globalMaxPerDayRange.max}
            min={globalMaxPerDayRange.min}
            onChange={(maxPerDay) => onChange({ maxPerDay })}
            value={draft.maxPerDay}
          />
        </div>
      </div>
    </Card>
  );
}
