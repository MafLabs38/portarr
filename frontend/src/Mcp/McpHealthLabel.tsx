import type { McpServer } from 'typings/Mcp';

import describeMcpHealth from './describeMcpHealth';
import styles from './McpHealthLabel.module.css';

const tones: Record<string, string | undefined> = { success: styles.success, danger: styles.danger };

interface McpHealthLabelProps {
  server: McpServer;
}

/** The health of a server as one pill: an icon and the words ("Healthy", "Down for 12 days"), never color alone. */
export default function McpHealthLabel({ server }: Readonly<McpHealthLabelProps>) {
  const { kind, icon: Icon, label } = describeMcpHealth(server);
  const tone = tones[kind] ?? styles.disabled;

  return (
    <span className={`${styles.label} ${tone}`}>
      <Icon aria-hidden="true" className={styles.icon} size={14} strokeWidth={2.5} />
      {label}
    </span>
  );
}
