import { NavLink } from 'react-router-dom';

import usePermissions from 'Auth/usePermissions';
import { CATALOG_WRITE, type PermissionOrAny, USERS_MANAGE } from 'Helpers/Props/permissions';
import translate from 'Utilities/String/translate';

import styles from './SettingsNav.module.css';

/** The pages of Settings, in the order they are offered; a role sees the ones it may open. */
export const settingsPages: { to: string; label: string; permission: PermissionOrAny }[] = [
  { to: '/settings/general', label: 'General', permission: CATALOG_WRITE },
  { to: '/settings/users', label: 'UsersAndAuth', permission: USERS_MANAGE },
];

/** Links between the pages of Settings while the sidebar has no second level. */
export default function SettingsNav() {
  const { can } = usePermissions();
  const pages = settingsPages.filter((page) => can(page.permission));

  if (pages.length < 2) {
    return null;
  }

  return (
    <nav aria-label={translate('Settings')} className={styles.nav}>
      {pages.map((page) => (
        <NavLink className={({ isActive }) => (isActive ? styles.active : styles.link)} key={page.to} to={page.to}>
          {translate(page.label)}
        </NavLink>
      ))}
    </nav>
  );
}
