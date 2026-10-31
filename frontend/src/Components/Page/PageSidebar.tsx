import type { LucideIcon } from 'lucide-react';
import { NavLink } from 'react-router-dom';

import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './PageSidebar.module.css';

interface Entry {
  to: string;
  label: string;
  icon: LucideIcon;
}

// Menu structure inspired by the *arr apps (spec §8.1); entries appear as their screens are built.
const entries: Entry[] = [
  { to: '/apps', label: 'Apps', icon: icons.APPS },
  { to: '/infrastructure', label: 'Infrastructure', icon: icons.INFRASTRUCTURE },
  { to: '/maintenance', label: 'Maintenance', icon: icons.MAINTENANCE },
  { to: '/system/status', label: 'System', icon: icons.SYSTEM },
];

export default function PageSidebar() {
  return (
    <nav aria-label={translate('MainMenu')} className={styles.sidebar}>
      {entries.map(({ to, label, icon: Icon }) => (
        <NavLink className={({ isActive }) => (isActive ? styles.activeLink : styles.link)} key={to} to={to}>
          <Icon aria-hidden="true" size={18} strokeWidth={2.25} />
          <span>{translate(label)}</span>
        </NavLink>
      ))}
    </nav>
  );
}
