import { ApiError } from 'Helpers/Hooks/useApiQuery';
import type {
  NotificationCategory,
  NotificationCategoryPolicy,
  NotificationLevel,
  NotificationSettings,
  NotificationSettingsUpdate,
} from 'typings/Notifications';
import translate from 'Utilities/String/translate';

/** What is typed for one category: the numbers stay text until they are sent, so an empty box is told, not turned into 0. */
export interface CategoryDraft {
  enabled: boolean;
  minimumLevel: NotificationLevel;
  reminderHours: string;
  maxReminders: string;
  maxPerDay: string;
}

export interface Draft {
  enabled: boolean;
  recipient: string;
  from: string;
  maxPerDay: string;
  /** By category id. */
  categories: Record<string, CategoryDraft>;
}

export type CategoryField = keyof CategoryDraft;
export type NumberField = 'reminderHours' | 'maxReminders' | 'maxPerDay';

/** The ranges the server checks, shown as hints. */
export const numberFields: { field: NumberField; label: string; help: string; min: number; max: number }[] = [
  {
    field: 'reminderHours',
    label: 'NotificationsReminderHours',
    help: 'NotificationsReminderHoursHelp',
    min: 1,
    max: 720,
  },
  { field: 'maxReminders', label: 'NotificationsMaxReminders', help: 'NotificationsMaxRemindersHelp', min: 0, max: 30 },
  {
    field: 'maxPerDay',
    label: 'NotificationsMaxPerDay',
    help: 'NotificationsCategoryMaxPerDayHelp',
    min: 1,
    max: 1000,
  },
];

export const globalMaxPerDayRange = { min: 1, max: 1000 };

/** The key a field error is kept under: `recipient`, `maxPerDay`, or `<category id>:<field>`. */
export const categoryKey = (id: string, field: CategoryField) => `${id}:${field}`;

export const categoryDraftOf = (policy: NotificationCategoryPolicy): CategoryDraft => ({
  enabled: policy.enabled,
  minimumLevel: policy.minimumLevel,
  reminderHours: String(policy.reminderHours),
  maxReminders: String(policy.maxReminders),
  maxPerDay: String(policy.maxPerDay),
});

export const draftOf = (settings: NotificationSettings): Draft => ({
  enabled: settings.enabled,
  recipient: settings.recipient ?? '',
  from: settings.from ?? '',
  maxPerDay: String(settings.maxPerDay),
  categories: Object.fromEntries(settings.categories.map((category) => [category.id, categoryDraftOf(category)])),
});

export const sameCategoryDraft = (a: CategoryDraft, b: CategoryDraft) =>
  (Object.keys(a) as CategoryField[]).every((field) => a[field] === b[field]);

export const isChanged = (stored: Draft, draft: Draft) => JSON.stringify(stored) !== JSON.stringify(draft);

const isWholeNumber = (text: string) => /^\d+$/.test(text.trim());

/** The errors of the last save: by field, the sentences about the settings as a whole, or a general failure. */
export interface Problems {
  fields: Record<string, string[]>;
  settings: string[];
  failure: string | null;
}

export const noProblems: Problems = { fields: {}, settings: [], failure: null };

const failureKeys: Record<number, string> = {
  401: 'NotificationsSessionEnded',
  403: 'NotificationsNotAllowed',
};

/** The numbers that are not whole numbers, told before the server is called. */
export function notNumbers(draft: Draft, categories: NotificationCategory[]): Problems | null {
  const fields: Record<string, string[]> = {};
  const message = [translate('NotificationsWholeNumber')];
  if (!isWholeNumber(draft.maxPerDay)) {
    fields.maxPerDay = message;
  }
  for (const { id } of categories) {
    const category = draft.categories[id];
    for (const { field } of numberFields) {
      if (category && !isWholeNumber(category[field])) {
        fields[categoryKey(id, field)] = message;
      }
    }
  }
  return Object.keys(fields).length > 0 ? { ...noProblems, fields } : null;
}

export function updateOf(draft: Draft, categories: NotificationCategory[]): NotificationSettingsUpdate {
  return {
    enabled: draft.enabled,
    recipient: draft.recipient.trim(),
    from: draft.from.trim(),
    maxPerDay: Number(draft.maxPerDay),
    categories: categories.flatMap(({ id }) => {
      const category = draft.categories[id];
      return category
        ? [
            {
              id,
              enabled: category.enabled,
              minimumLevel: category.minimumLevel,
              reminderHours: Number(category.reminderHours),
              maxReminders: Number(category.maxReminders),
              maxPerDay: Number(category.maxPerDay),
            },
          ]
        : [];
    }),
  };
}

const globalFields = new Set(['enabled', 'recipient', 'from', 'maxperday']);
const globalFieldName: Record<string, string> = {
  enabled: 'enabled',
  recipient: 'recipient',
  from: 'from',
  maxperday: 'maxPerDay',
};
const categoryFieldName: Record<string, CategoryField> = {
  enabled: 'enabled',
  minimumlevel: 'minimumLevel',
  reminderhours: 'reminderHours',
  maxreminders: 'maxReminders',
  maxperday: 'maxPerDay',
};

/**
 * Where the server's key points: a global field (`recipient`), a field of a category (`categories.data.reminderHours`)
 * or nothing the page shows (`settings`, `categories`, `categories[0].id`), which goes to the top of the form. Keys are
 * compared without case: the server may name a property or its JSON name.
 */
function targetOf(key: string, categories: NotificationCategory[]): string | null {
  const lower = key.toLowerCase();
  if (globalFields.has(lower)) {
    return globalFieldName[lower] ?? null;
  }
  const match = /^categories\.(.+)\.([^.]+)$/i.exec(key);
  const category = categories.find(({ id }) => id.toLowerCase() === match?.[1]?.toLowerCase());
  const field = categoryFieldName[match?.[2]?.toLowerCase() ?? ''];
  return category && field ? categoryKey(category.id, field) : null;
}

export function problemsOf(error: unknown, categories: NotificationCategory[]): Problems {
  if (!(error instanceof ApiError)) {
    return { ...noProblems, failure: 'NotificationsSaveFailed' };
  }
  if (error.status !== 400 || !error.errors) {
    return { ...noProblems, failure: failureKeys[error.status] ?? 'NotificationsSaveFailed' };
  }

  const problems: Problems = { fields: {}, settings: [], failure: null };
  for (const [key, messages] of Object.entries(error.errors)) {
    const target = targetOf(key, categories);
    if (target) {
      problems.fields[target] = messages;
    } else {
      problems.settings.push(...messages);
    }
  }
  return problems;
}
