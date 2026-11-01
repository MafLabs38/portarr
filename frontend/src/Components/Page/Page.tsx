import type { ReactNode } from 'react';

import ChatPanel from 'Chat/ChatPanel';

import styles from './Page.module.css';
import PageHeader from './PageHeader';
import PageSidebar from './PageSidebar';
import UnreachableNotice from './UnreachableNotice';

interface PageProps {
  children: ReactNode;
}

export default function Page({ children }: Readonly<PageProps>) {
  return (
    <div className={styles.page}>
      <PageHeader />
      <UnreachableNotice />
      <div className={styles.main}>
        <PageSidebar />
        {children}
        <ChatPanel />
      </div>
    </div>
  );
}
