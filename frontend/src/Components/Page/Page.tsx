import type { ReactNode } from 'react';

import styles from './Page.module.css';
import PageHeader from './PageHeader';
import PageSidebar from './PageSidebar';

interface PageProps {
  children: ReactNode;
}

export default function Page({ children }: Readonly<PageProps>) {
  return (
    <div className={styles.page}>
      <PageHeader />
      <div className={styles.main}>
        <PageSidebar />
        {children}
      </div>
    </div>
  );
}
