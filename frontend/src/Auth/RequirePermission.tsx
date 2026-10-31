import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';

import { firstAllowedPath } from './navigation';
import usePermissions from './usePermissions';

interface RequirePermissionProps {
  permission: string;
  children: ReactNode;
}

/** A route only for roles that hold a permission; the others go to the first page their role allows. */
export default function RequirePermission({ permission, children }: Readonly<RequirePermissionProps>) {
  const { me, can } = usePermissions();

  if (!me) {
    return null;
  }

  return can(permission) ? children : <Navigate replace to={firstAllowedPath(me.permissions)} />;
}
