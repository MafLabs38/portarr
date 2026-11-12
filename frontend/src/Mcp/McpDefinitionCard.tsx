import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import type { McpServer } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import describeMcpHealth from './describeMcpHealth';
import McpHealthLabel from './McpHealthLabel';
import styles from './McpServerDetail.module.css';

interface McpDefinitionCardProps {
  server: McpServer;
}

function describeValues(server: McpServer): string {
  return server.values
    .map((value) => {
      if (!value.variable) {
        return `${value.name} = ${value.literal ?? ''}`;
      }
      const from = value.secret ? ` (${translate('McpFromVault', { key: value.secret })})` : '';
      return `${value.name} ← $${value.variable}${from}`;
    })
    .join('\n');
}

/** What the catalog says about a server, and how it is doing. Read-only: the definition is edited in the catalog file (D-112). */
export default function McpDefinitionCard({ server }: Readonly<McpDefinitionCardProps>) {
  const { detail } = describeMcpHealth(server);
  const local = server.transport === 'stdio';

  return (
    <section aria-label={translate('McpDefinition')} className={styles.card}>
      <h2 className={styles.cardTitle}>{translate('McpDefinition')}</h2>
      <DescriptionList>
        <DescriptionListItem
          data={
            <>
              <McpHealthLabel server={server} /> {detail}
            </>
          }
          title={translate('McpHealth')}
        />
        <DescriptionListItem data={translate(local ? 'McpLocal' : 'McpRemote')} title={translate('McpTransport')} />
        <DescriptionListItem
          data={local ? [server.command, ...server.arguments].join(' ') : server.url}
          monospace
          title={translate(local ? 'McpCommand' : 'McpAddress')}
        />
        <DescriptionListItem data={server.scope} title={translate('McpDefaultScope')} />
        {server.values.length > 0 ? (
          <DescriptionListItem
            data={<span className={styles.values}>{describeValues(server)}</span>}
            monospace
            title={translate(local ? 'McpEnvironment' : 'McpHeaders')}
          />
        ) : null}
        {server.app ? <DescriptionListItem data={server.app} title={translate('McpApp')} /> : null}
        {server.notes ? <DescriptionListItem data={server.notes} title={translate('McpNotes')} /> : null}
      </DescriptionList>
      <p className={styles.hint}>{translate('McpEditInCatalog')}</p>
    </section>
  );
}
