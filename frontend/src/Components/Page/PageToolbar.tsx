import type { ReactNode } from 'react';

import styles from './PageToolbar.module.css';

interface PageToolbarProps {
  /** Actions on the left. */
  left?: ReactNode;
  /** View options on the right (DESIGN.md §2). */
  right?: ReactNode;
}

export default function PageToolbar({ left, right }: Readonly<PageToolbarProps>) {
  return (
    <div className={styles.toolbar} role="toolbar">
      <div className={styles.section}>{left}</div>
      <div className={styles.section}>{right}</div>
    </div>
  );
}
