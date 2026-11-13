import Alert from 'Components/Alert/Alert';
import type { McpServer } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import styles from './McpIndex.module.css';
import McpServerRow from './McpServerRow';

interface McpServersTabProps {
  servers: McpServer[] | undefined;
}

/** The servers of the catalog, each on one line with its health. */
export default function McpServersTab({ servers }: Readonly<McpServersTabProps>) {
  return (
    <>
      <p className={styles.intro}>{translate('McpIntro')}</p>

      {servers?.length === 0 ? <Alert kind="info">{translate('McpEmpty')}</Alert> : null}

      {servers && servers.length > 0 ? (
        <ul aria-label={translate('McpServers')} className={styles.list}>
          {servers.map((server) => (
            <McpServerRow key={server.id} server={server} />
          ))}
        </ul>
      ) : null}
    </>
  );
}
