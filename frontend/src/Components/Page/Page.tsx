import type { ReactNode } from 'react';

import styles from './Page.module.css';
import PageHeader from './PageHeader';

interface PageProps {
  children: ReactNode;
}

export default function Page({ children }: PageProps) {
  return (
    <div className={styles.page}>
      <PageHeader />
      <div className={styles.main}>{children}</div>
    </div>
  );
}
