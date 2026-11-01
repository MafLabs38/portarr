// The permissions of the API (V7, D-93), by the key it checks. A role is a set of them; the server enforces each one and
// the UI only decides what to show. Keep in step with Portarr.Core/Accounts/Permissions.cs.
export const APPS_VIEW = 'apps.view';
export const INFRASTRUCTURE_VIEW = 'infrastructure.view';
export const DATA_VIEW = 'data.view';
export const MAINTENANCE_VIEW = 'maintenance.view';
export const ASSISTANT_VIEW = 'assistant.view';
export const CHAT_USE = 'chat.use';
export const CATALOG_WRITE = 'catalog.write';
export const SYSTEM_VIEW = 'system.view';
export const SETTINGS_MANAGE = 'settings.manage';
export const USERS_MANAGE = 'users.manage';
export const SECRETS_VIEW = 'secrets.view';

export type Permission =
  | typeof APPS_VIEW
  | typeof INFRASTRUCTURE_VIEW
  | typeof DATA_VIEW
  | typeof MAINTENANCE_VIEW
  | typeof ASSISTANT_VIEW
  | typeof CHAT_USE
  | typeof CATALOG_WRITE
  | typeof SYSTEM_VIEW
  | typeof SETTINGS_MANAGE
  | typeof USERS_MANAGE
  | typeof SECRETS_VIEW;
