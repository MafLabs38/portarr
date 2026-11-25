import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import type { NotificationCategory, NotificationLevel } from 'typings/Notifications';
import translate from 'Utilities/String/translate';

import {
  type CategoryDraft,
  categoryDraftOf,
  type CategoryField,
  categoryKey,
  numberFields,
  type Problems,
  sameCategoryDraft,
} from './notificationDraft';
import styles from './Notifications.module.css';
import NumberInput from './NumberInput';

export const levels: NotificationLevel[] = ['info', 'warning', 'danger'];

interface CategoryCardProps {
  category: NotificationCategory;
  draft: CategoryDraft;
  problems: Problems;
  onChange: (patch: Partial<CategoryDraft>) => void;
}

/** One category: whether it writes, from which level, how often and how many mails. Nothing is saved until the page is. */
export default function CategoryCard({ category, draft, problems, onChange }: Readonly<CategoryCardProps>) {
  const { id, defaults } = category;
  const defaultDraft = categoryDraftOf(defaults);
  const atDefaults = sameCategoryDraft(draft, defaultDraft);
  const errorsOf = (field: CategoryField) => problems.fields[categoryKey(id, field)];

  return (
    <Card title={category.title}>
      <div className={styles.categoryHeader}>
        <p className={styles.help}>{category.description}</p>
        {draft.enabled ? null : <span className={styles.badge}>{translate('NotificationsOff')}</span>}
      </div>
      <div className={styles.fields}>
        <label className={styles.checkbox}>
          <input
            checked={draft.enabled}
            onChange={(event) => onChange({ enabled: event.target.checked })}
            type="checkbox"
          />
          {translate('NotificationsCategoryEnabled')}
        </label>
        {errorsOf('enabled')?.map((message) => (
          <p className={styles.fieldError} key={message}>
            {message}
          </p>
        ))}
        <div className={styles.grid}>
          <div className={form.field}>
            <label className={form.label} htmlFor={`notifications-${id}-minimumLevel`}>
              {translate('NotificationsMinimumLevel')}
            </label>
            <select
              aria-describedby={`notifications-${id}-minimumLevel-help`}
              aria-invalid={errorsOf('minimumLevel') ? true : undefined}
              className={form.input}
              id={`notifications-${id}-minimumLevel`}
              onChange={(event) => onChange({ minimumLevel: event.target.value as NotificationLevel })}
              value={draft.minimumLevel}
            >
              {levels.map((level) => (
                <option key={level} value={level}>
                  {translate(`NotificationsLevel_${level}`)}
                </option>
              ))}
            </select>
            <span className={form.help} id={`notifications-${id}-minimumLevel-help`}>
              {translate('NotificationsMinimumLevelHelp')}
            </span>
            {errorsOf('minimumLevel')?.map((message) => (
              <p className={styles.fieldError} key={message}>
                {message}
              </p>
            ))}
          </div>
          {numberFields.map(({ field, label, help, min, max }) => (
            <NumberInput
              errors={errorsOf(field)}
              help={translate(help, { min, max })}
              id={`notifications-${id}-${field}`}
              key={field}
              label={translate(label)}
              max={max}
              min={min}
              onChange={(value) => onChange({ [field]: value })}
              value={draft[field]}
            />
          ))}
        </div>
        <div className={form.actions}>
          <button className={form.secondary} disabled={atDefaults} onClick={() => onChange(defaultDraft)} type="button">
            {translate('NotificationsResetDefaults')}
          </button>
          <span className={form.help}>
            {translate('NotificationsDefaultsHint', {
              state: translate(defaults.enabled ? 'NotificationsOn' : 'NotificationsOff'),
              level: translate(`NotificationsLevel_${defaults.minimumLevel}`),
              hours: defaults.reminderHours,
              reminders: defaults.maxReminders,
              perDay: defaults.maxPerDay,
            })}
          </span>
        </div>
      </div>
    </Card>
  );
}
