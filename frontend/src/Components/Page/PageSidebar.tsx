import { NavLink } from 'react-router';

import { menuEntries } from 'Auth/navigation';
import usePermissions from 'Auth/usePermissions';
import translate from 'Utilities/String/translate';

import styles from './PageSidebar.module.css';
import PortalBadge from './PortalBadge';

/** The menu shows the entries the signed-in user's role allows (V7): the same role, another view. */
export default function PageSidebar() {
  const { can } = usePermissions();

  return (
    <aside className={styles.sidebar}>
      <nav aria-label={translate('MainMenu')} className={styles.menu}>
        {menuEntries
          .filter((entry) => can(entry.permission))
          .map(({ to, label, icon: Icon }) => (
            <NavLink className={({ isActive }) => (isActive ? styles.activeLink : styles.link)} key={to} to={to}>
              <Icon aria-hidden="true" size={18} strokeWidth={2.25} />
              <span>{translate(label)}</span>
            </NavLink>
          ))}
      </nav>
      <PortalBadge />
    </aside>
  );
}
