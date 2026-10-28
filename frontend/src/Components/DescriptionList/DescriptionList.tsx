import type { ReactNode } from 'react';

import styles from './DescriptionList.module.css';

interface DescriptionListProps {
  children: ReactNode;
}

export default function DescriptionList({ children }: DescriptionListProps) {
  return <dl className={styles.list}>{children}</dl>;
}
