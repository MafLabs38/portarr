import { Navigate } from 'react-router';

import { firstAllowedPath } from './navigation';
import usePermissions from './usePermissions';

/** The root path: the first page the role allows. */
export default function Home() {
  const { me } = usePermissions();

  return me ? <Navigate replace to={firstAllowedPath(me.permissions)} /> : null;
}
