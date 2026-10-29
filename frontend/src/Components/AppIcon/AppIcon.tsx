import { useState } from 'react';

import styles from './AppIcon.module.css';

// Served by Portarr: the operator's icons first, then the community set, cached server-side (D-47).
const ICON_BASE_URL = '/api/v1/icons';

interface AppIconProps {
  name: string;
  icon: string | null;
  size: number;
}

/** An app's logo, falling back to its initial when no icon is set or found (DESIGN.md §4.2). */
export default function AppIcon({ name, icon, size }: AppIconProps) {
  const [failed, setFailed] = useState(false);
  const style = { width: size, height: size };

  if (!icon || failed) {
    return (
      <span aria-hidden="true" className={styles.fallback} style={{ ...style, fontSize: size * 0.45 }}>
        {name.charAt(0).toUpperCase()}
      </span>
    );
  }

  return (
    <img
      alt=""
      className={styles.icon}
      onError={() => setFailed(true)}
      src={`${ICON_BASE_URL}/${encodeURIComponent(icon)}`}
      style={style}
    />
  );
}
