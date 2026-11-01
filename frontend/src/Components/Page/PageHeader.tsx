import { Link } from 'react-router';

import UserMenu from 'Auth/UserMenu';
import ChatToggle from 'Chat/ChatToggle';

import styles from './PageHeader.module.css';
import PortholeLogo from './PortholeLogo';

export default function PageHeader() {
  return (
    <header className={styles.header}>
      <Link className={styles.brand} to="/">
        <PortholeLogo size={32} />
        <span className={styles.name}>Portarr</span>
      </Link>
      <ChatToggle />
      <UserMenu />
    </header>
  );
}
