/** From which level a finding is worth a mail. */
export type NotificationLevel = 'info' | 'warning' | 'danger';

/** `category` for the mail of a category, `test` for the test mail. */
export type NotificationLogKind = 'category' | 'test';

/** One mail that was tried, delivered or not. */
export interface NotificationLogEntry {
  id: number;
  kind: NotificationLogKind;
  /** The id of the category (`data`); null for a test mail. */
  category: string | null;
  /** The worst level of the findings the mail is about. */
  level: NotificationLevel;
  /** 0 for the first mail of an episode, 1 and up for its reminders. */
  reminder: number;
  sentAt: string;
  delivered: boolean;
  /** Why the mail did not leave, in a sentence; null when it was delivered. */
  error: string | null;
}

export interface NotificationLog {
  entries: NotificationLogEntry[];
}

/** Where the mail goes out, as deployed: read-only here. The password is a file, so only whether there is one is told. */
export interface NotificationConnection {
  provider: string;
  host: string | null;
  port: number;
  userName: string | null;
  startTls: boolean;
  passwordFileConfigured: boolean;
}

/** What the operator tunes for one category. */
export interface NotificationCategoryPolicy {
  enabled: boolean;
  minimumLevel: NotificationLevel;
  reminderHours: number;
  maxReminders: number;
  maxPerDay: number;
}

/** One area of the attention findings (`apps`, `infrastructure`, `data`, `maintenance`), with its settings and its defaults. */
export interface NotificationCategory extends NotificationCategoryPolicy {
  id: string;
  title: string;
  description: string;
  defaults: NotificationCategoryPolicy;
}

export interface NotificationSettings {
  enabled: boolean;
  recipient: string | null;
  from: string | null;
  /** The most mails in 24 hours, all categories together. */
  maxPerDay: number;
  connection: NotificationConnection;
  /** What is wrong with the settings while they are on; null when fine. */
  problem: string | null;
  /** What is wrong with the password file; null when fine. */
  secretProblem: string | null;
  lastAttempt: NotificationLogEntry | null;
  categories: NotificationCategory[];
}

export interface NotificationCategoryUpdate extends NotificationCategoryPolicy {
  id: string;
}

/** What the page changes (every field is required by the API). */
export interface NotificationSettingsUpdate {
  enabled: boolean;
  recipient: string;
  from: string;
  maxPerDay: number;
  categories: NotificationCategoryUpdate[];
}

export interface NotificationTestResult {
  ok: boolean;
  error: string | null;
}
