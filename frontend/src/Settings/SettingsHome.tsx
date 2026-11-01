import { Navigate } from 'react-router-dom';

import usePermissions from 'Auth/usePermissions';

import { settingsPages } from './SettingsNav';

/** /settings opens the first page of Settings the role may open. */
export default function SettingsHome() {
  const { me, can } = usePermissions();

  if (!me) {
    return null;
  }

  return <Navigate replace to={settingsPages.find((page) => can(page.permission))?.to ?? '/'} />;
}
