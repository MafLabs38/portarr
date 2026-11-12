import { Link } from 'react-router';

import type { McpServer } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import describeMcpHealth from './describeMcpHealth';
import McpHealthLabel from './McpHealthLabel';
import styles from './McpServerRow.module.css';

interface McpServerRowProps {
  server: McpServer;
}

/** One MCP server on one line: its name, where it is (address or command), and whether it is up and since when. */
export default function McpServerRow({ server }: Readonly<McpServerRowProps>) {
  const { detail } = describeMcpHealth(server);
  const where = server.url ?? [server.command, ...server.arguments].filter(Boolean).join(' ');

  return (
    <li aria-label={server.name} className={styles.row}>
      <div className={styles.what}>
        <Link className={styles.name} to={`/mcp-servers/${encodeURIComponent(server.id)}`}>
          {server.name}
        </Link>
        <span className={styles.tag}>{translate(server.transport === 'http' ? 'McpRemote' : 'McpLocal')}</span>
      </div>
      <div className={styles.where}>
        <span className={styles.address} title={where}>
          {where}
        </span>
      </div>
      <div className={styles.health}>
        <McpHealthLabel server={server} />
        {detail ? <span className={styles.detail}>{detail}</span> : null}
      </div>
    </li>
  );
}
