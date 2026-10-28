import { useState } from 'react';

import styles from './AppIcon.module.css';

// Community icon set used by most homelab dashboards; names follow the app ("jellyfin", "grafana").
const ICON_BASE_URL = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg';

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

  const src = /^https?:\/\//.test(icon) ? icon : `${ICON_BASE_URL}/${encodeURIComponent(icon)}.svg`;

  return <img alt="" className={styles.icon} onError={() => setFailed(true)} src={src} style={style} />;
}
