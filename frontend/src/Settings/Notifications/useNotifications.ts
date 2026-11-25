import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type {
  NotificationLog,
  NotificationSettings,
  NotificationSettingsUpdate,
  NotificationTestResult,
} from 'typings/Notifications';

const settingsPath = '/notifications/settings';
const logPath = '/notifications/log';
const testPath = '/notifications/test';

export function useNotificationSettings() {
  return useApiQuery<NotificationSettings>({ path: settingsPath });
}

export function useNotificationLog() {
  return useApiQuery<NotificationLog>({ path: logPath });
}

/** The answer is the settings now in force: it replaces the cached ones. */
export function useSaveNotificationSettings() {
  return useApiMutation<NotificationSettingsUpdate, NotificationSettings>({ path: settingsPath, method: 'PUT' });
}

/** A test mail counts in the log (and in the daily limit), so the log is read again afterwards. */
export function useSendTestMail() {
  return useApiMutation<undefined, NotificationTestResult>({
    path: testPath,
    method: 'POST',
    invalidate: [logPath, settingsPath],
  });
}
