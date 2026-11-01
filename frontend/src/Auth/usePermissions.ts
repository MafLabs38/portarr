import type { PermissionOrAny } from 'Helpers/Props/permissions';

import { useMe } from './useAuth';

/** Whether the list of permissions holds the one asked for, or any one of those asked for. */
export const holdsAny = (held: readonly string[], required: PermissionOrAny) =>
  (typeof required === 'string' ? [required] : required).some((permission) => held.includes(permission));

/** What the signed-in user's role allows (V7). The API enforces it again: this only decides what the UI shows. */
export default function usePermissions() {
  const { data: me } = useMe();

  return {
    me,
    loaded: me !== undefined,
    can: (permission: PermissionOrAny) => holdsAny(me?.permissions ?? [], permission),
  };
}
