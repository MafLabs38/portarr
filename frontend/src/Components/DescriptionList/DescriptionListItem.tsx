import type { ReactNode } from 'react';

import styles from './DescriptionListItem.module.css';

interface DescriptionListItemProps {
  title: string;
  data: ReactNode;
  /** Technical values (versions, timestamps) use the monospace font (DESIGN.md §6.4). */
  monospace?: boolean;
}

export default function DescriptionListItem({ title, data, monospace = false }: DescriptionListItemProps) {
  return (
    <>
      <dt className={styles.title}>{title}</dt>
      <dd className={monospace ? styles.monospaceData : styles.data}>{data}</dd>
    </>
  );
}
