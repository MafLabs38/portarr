import type { LucideIcon } from 'lucide-react';
import { Link } from 'react-router-dom';

import type * as kinds from 'Helpers/Props/kinds';

import styles from './SummaryTile.module.css';

export interface Tile {
  key: string;
  value: string;
  label: string;
  kind: kinds.Kind;
  icon: LucideIcon;
  /** A line under the figure (the part of it still pending). */
  detail?: string;
  /** A query string (`?tab=images&filter=behind`): the tile becomes a link that filters the page. */
  to?: string;
  /** The tile's filter is the one applied. */
  active?: boolean;
}

/** A figure of a summary: icon, value and label, colored by kind; a link when it filters its page. */
export default function SummaryTile({ tile }: Readonly<{ tile: Tile }>) {
  const Icon = tile.icon;
  const content = (
    <>
      <Icon aria-hidden="true" className={styles.icon} size={18} strokeWidth={2.25} />
      <span className={styles.value}>{tile.value}</span>
      <span className={styles.label}>{tile.label}</span>
      {tile.detail ? <span className={styles.detail}>{tile.detail}</span> : null}
    </>
  );

  return (
    <li
      aria-label={`${tile.label}: ${tile.value}`}
      className={`${styles.tile} ${styles[tile.kind]} ${tile.active ? styles.active : ''}`}
    >
      {tile.to === undefined ? (
        <span className={styles.content}>{content}</span>
      ) : (
        <Link aria-current={tile.active ? 'true' : undefined} className={styles.link} to={{ search: tile.to }}>
          {content}
        </Link>
      )}
    </li>
  );
}
