import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';

export interface MenuEntry {
  to: string;
  label: string;
  icon: LucideIcon;
  /** The permission that shows the entry and opens its page (V7); the API checks it again. */
  permission: string;
}

// Menu structure inspired by the *arr apps (spec §8.1); entries appear as their screens are built. Each one is
// shown to the roles that hold its permission, so a role is also "a style of view" (V7).
export const menuEntries: MenuEntry[] = [
  { to: '/apps', label: 'Apps', icon: icons.APPS, permission: 'apps.view' },
  { to: '/infrastructure', label: 'Infrastructure', icon: icons.INFRASTRUCTURE, permission: 'infrastructure.view' },
  { to: '/data', label: 'Data', icon: icons.DATA, permission: 'data.view' },
  { to: '/maintenance', label: 'Maintenance', icon: icons.MAINTENANCE, permission: 'maintenance.view' },
  // Its own entry while the sidebar has no second level (C3): the same questions, asked from an AI (V5).
  { to: '/assistant', label: 'Assistant', icon: icons.ASSISTANT, permission: 'assistant.view' },
  { to: '/system/status', label: 'System', icon: icons.SYSTEM, permission: 'system.view' },
  // One page with tabs (Users, Roles, Activity) while the sidebar has no second level.
  { to: '/settings/users', label: 'Settings', icon: icons.SETTINGS, permission: 'users.manage' },
];

/** Where a user lands: the first page their role allows (their account page when it allows none). */
export function firstAllowedPath(permissions: readonly string[]): string {
  return menuEntries.find((entry) => permissions.includes(entry.permission))?.to ?? '/account/profile';
}
