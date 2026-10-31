import type { LucideIcon } from 'lucide-react';

import styles from './SummaryLine.module.css';

export type Tone = 'ok' | 'bad' | 'warn' | 'muted';

interface SummaryLineProps {
  icon: LucideIcon;
  label: string;
  value: string;
  tone: Tone;
  title?: string;
}

/** One line of a tile: an icon instead of a text label, so the value keeps the tile's width; the label stays accessible. */
export default function SummaryLine({ icon: Icon, label, value, tone, title }: Readonly<SummaryLineProps>) {
  return (
    <div className={`${styles.line} ${styles[tone]}`} title={title ?? `${label}: ${value}`}>
      <Icon aria-label={label} role="img" size={13} strokeWidth={2.5} />
      <span className={styles.value}>{value}</span>
    </div>
  );
}
