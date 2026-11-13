import { Link } from 'react-router';

import Alert from 'Components/Alert/Alert';
import * as icons from 'Helpers/Props/icons';
import type { McpServer, McpWorkstation } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import styles from './McpWorkstationsTab.module.css';

interface McpWorkstationsTabProps {
  workstations: McpWorkstation[] | undefined;
  servers: McpServer[] | undefined;
  isError: boolean;
}

/**
 * The workstations in rows and the servers in columns: a cell says in which assistants the server is wanted on that machine, or
 * that it is not. Declarative (D-110): nothing says whether it is installed. A row opens the machine's plan.
 */
export default function McpWorkstationsTab({ workstations, servers, isError }: Readonly<McpWorkstationsTabProps>) {
  return (
    <>
      <p className={styles.intro}>{translate('McpWorkstationsIntro')}</p>

      {isError ? <Alert kind="danger">{translate('ErrorLoadingMcpWorkstations')}</Alert> : null}
      {workstations?.length === 0 ? <Alert kind="info">{translate('McpWorkstationsEmpty')}</Alert> : null}

      {workstations && workstations.length > 0 && servers ? (
        <div className={styles.scroller}>
          <table aria-label={translate('McpWantedMatrix')} className={styles.matrix}>
            <thead>
              <tr>
                <th scope="col">{translate('McpWorkstation')}</th>
                {servers.map((server) => (
                  <th key={server.id} scope="col">
                    <Link to={`/mcp-servers/${encodeURIComponent(server.id)}`}>{server.name}</Link>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {workstations.map((workstation) => (
                <tr key={workstation.id}>
                  <th scope="row">
                    <Link to={`/mcp-servers/workstations/${encodeURIComponent(workstation.id)}`}>
                      {workstation.name}
                    </Link>
                    <span className={styles.os}>{translate(`McpOs_${workstation.os}`)}</span>
                  </th>
                  {servers.map((server) => {
                    const wanted = workstation.wanted.find((w) => w.server === server.id);
                    return (
                      <td key={server.id}>
                        {wanted ? (
                          <span className={styles.wanted} title={wanted.clients.join(', ')}>
                            <icons.HEALTHY aria-hidden="true" size={16} strokeWidth={2.5} />
                            <span className={styles.hidden}>
                              {translate('McpWantedIn', { clients: wanted.clients.join(', ') })}
                            </span>
                          </span>
                        ) : (
                          <span className={styles.none}>
                            <span aria-hidden="true">—</span>
                            <span className={styles.hidden}>{translate('McpNotWanted')}</span>
                          </span>
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}
