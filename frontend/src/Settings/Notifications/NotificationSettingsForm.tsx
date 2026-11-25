import { type FormEvent, useState } from 'react';

import form from 'Components/Form/Form.module.css';
import type { NotificationSettings } from 'typings/Notifications';
import translate from 'Utilities/String/translate';

import CategoryCard from './CategoryCard';
import MailSettingsCard from './MailSettingsCard';
import {
  type CategoryDraft,
  type Draft,
  draftOf,
  isChanged,
  noProblems,
  notNumbers,
  type Problems,
  problemsOf,
  updateOf,
} from './notificationDraft';
import styles from './Notifications.module.css';
import { useSaveNotificationSettings } from './useNotifications';

/**
 * Whether Portarr writes, to whom, and for each category from which level, how often and how many (D-129, F-NOT-01).
 * One Save for the whole page. The mail server is not here: it is a setting of the deployment (see the connection card).
 * The server checks every value; the page only refuses what is not a number.
 */
export default function NotificationSettingsForm({ settings }: Readonly<{ settings: NotificationSettings }>) {
  const save = useSaveNotificationSettings();
  const [draft, setDraft] = useState<Draft>(() => draftOf(settings));
  const [problems, setProblems] = useState<Problems>(noProblems);
  const [saved, setSaved] = useState(false);

  const changed = isChanged(draftOf(settings), draft);

  const change = (patch: Partial<Draft>) => {
    setDraft((current) => ({ ...current, ...patch }));
    setSaved(false);
  };

  const changeCategory = (id: string, patch: Partial<CategoryDraft>) => {
    setDraft((current) => {
      const category = current.categories[id];
      return category
        ? { ...current, categories: { ...current.categories, [id]: { ...category, ...patch } } }
        : current;
    });
    setSaved(false);
  };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setSaved(false);

    const refused = notNumbers(draft, settings.categories);
    if (refused) {
      setProblems(refused);
      return;
    }

    try {
      const result = await save.mutateAsync(updateOf(draft, settings.categories));
      setDraft(draftOf(result));
      setProblems(noProblems);
      setSaved(true);
    } catch (error) {
      setProblems(problemsOf(error, settings.categories));
    }
  };

  return (
    <form className={form.form} noValidate onSubmit={onSubmit}>
      {problems.settings.length > 0 ? (
        <div className={styles.formError} role="alert">
          {problems.settings.map((message) => (
            <p className={form.error} key={message}>
              {message}
            </p>
          ))}
        </div>
      ) : null}
      <MailSettingsCard draft={draft} onChange={change} problems={problems} />
      <h2 className={styles.sectionTitle}>{translate('NotificationsCategories')}</h2>
      <p className={styles.help}>{translate('NotificationsCategoriesHelp')}</p>
      {settings.categories.map((category) => {
        const categoryDraft = draft.categories[category.id];
        return categoryDraft ? (
          <CategoryCard
            category={category}
            draft={categoryDraft}
            key={category.id}
            onChange={(patch) => changeCategory(category.id, patch)}
            problems={problems}
          />
        ) : null;
      })}
      <div className={form.actions}>
        <button className={form.save} disabled={!changed || save.isPending} type="submit">
          {translate('Save')}
        </button>
        {saved ? <output className={form.saved}>{translate('NotificationsSaved')}</output> : null}
      </div>
      {problems.failure ? (
        <p className={form.error} role="alert">
          {translate(problems.failure)}
        </p>
      ) : null}
    </form>
  );
}
