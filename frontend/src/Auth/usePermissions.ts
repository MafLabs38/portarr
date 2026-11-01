import type { Permission } from 'Helpers/Props/permissions';

import { useMe } from './useAuth';

/** What the signed-in user's role allows (V7). The API enforces it again: this only decides what the UI shows. */
export default function usePermissions() {
  const { data: me } = useMe();

  return {
    me,
    loaded: me !== undefined,
    can: (permission: Permission) => me?.permissions.includes(permission) ?? false,
  };
}
