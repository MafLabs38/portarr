import { type KeyboardEvent, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';

import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import Avatar from './Avatar';
import { useLogout } from './useAuth';
import usePermissions from './usePermissions';
import styles from './UserMenu.module.css';

/**
 * The account button at the top right of the header, like a Google account: the user's round picture opens a menu with
 * who they are (display name, user name, role), Profile, API keys and Log out (V7, D-95).
 */
export default function UserMenu() {
  const { me } = usePermissions();
  const logout = useLogout();
  const [open, setOpen] = useState(false);
  const wrapper = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const close = (event: Event) => {
      if (!wrapper.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [open]);

  if (!me) {
    return null;
  }

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      setOpen(false);
      wrapper.current?.querySelector('button')?.focus();
    }
  };

  return (
    <div className={styles.wrapper} onKeyDown={onKeyDown} ref={wrapper}>
      <button
        aria-expanded={open}
        aria-haspopup="menu"
        className={styles.button}
        onClick={() => setOpen(!open)}
        type="button"
      >
        <Avatar avatarUrl={me.avatarUrl} displayName={me.displayName} size={32} username={me.username} />
        {/* The name comes from this text, not from an aria-label: the initials in the avatar are visible text that a label
            must contain (WCAG 2.5.3), and they are not a name. */}
        <span className={styles.visuallyHidden}>{translate('AccountMenu', { name: me.displayName })}</span>
      </button>
      {open ? (
        <div aria-label={translate('Account')} className={styles.menu} role="menu">
          <div className={styles.who}>
            <Avatar avatarUrl={me.avatarUrl} displayName={me.displayName} size={44} username={me.username} />
            <div className={styles.names}>
              <span className={styles.displayName}>{me.displayName}</span>
              <span className={styles.meta}>{me.username}</span>
              <span className={styles.meta}>{translate('RoleName', { role: me.role })}</span>
            </div>
          </div>
          <Link className={styles.item} onClick={() => setOpen(false)} role="menuitem" to="/account/profile">
            <icons.USER aria-hidden="true" size={16} strokeWidth={2.25} />
            {translate('Profile')}
          </Link>
          <Link className={styles.item} onClick={() => setOpen(false)} role="menuitem" to="/account/api-keys">
            <icons.API_KEY aria-hidden="true" size={16} strokeWidth={2.25} />
            {translate('ApiKeys')}
          </Link>
          <button className={styles.item} onClick={() => logout.mutate(undefined)} role="menuitem" type="button">
            <icons.LOG_OUT aria-hidden="true" size={16} strokeWidth={2.25} />
            {translate('LogOut')}
          </button>
        </div>
      ) : null}
    </div>
  );
}
