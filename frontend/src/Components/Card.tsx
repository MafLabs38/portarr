import type { ReactNode } from 'react';

import styles from './Card.module.css';

interface CardProps {
  title: string;
  children: ReactNode;
}

export default function Card({ title, children }: Readonly<CardProps>) {
  return (
    <section aria-label={title} className={styles.card}>
      <h2 className={styles.title}>{title}</h2>
      {children}
    </section>
  );
}
