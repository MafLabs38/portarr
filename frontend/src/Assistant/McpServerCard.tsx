import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import type AssistantStatus from 'typings/Assistant';
import translate from 'Utilities/String/translate';

import styles from './AssistantIndex.module.css';

interface McpServerCardProps {
  status: AssistantStatus;
  endpoint: string;
}

/** Whether the MCP server is on, where it listens, how it authenticates, and what it offers (write tools marked). */
export default function McpServerCard({ status, endpoint }: Readonly<McpServerCardProps>) {
  return (
    <Card title={translate('McpServer')}>
      <DescriptionList>
        <DescriptionListItem
          data={
            <span className={status.enabled ? styles.on : styles.off}>
              {status.enabled ? translate('McpEnabled') : translate('McpDisabled')}
            </span>
          }
          title={translate('State')}
        />
        <DescriptionListItem data={endpoint} monospace title={translate('Endpoint')} />
        <DescriptionListItem data={translate('McpTransportValue')} title={translate('Transport')} />
        <DescriptionListItem
          data={
            status.keyConfigured
              ? translate('McpKeyConfigured', { header: status.headerName })
              : translate('McpKeyMissing')
          }
          title={translate('Authentication')}
        />
        <DescriptionListItem
          data={translate(status.writeTools ? 'McpAccessWriteValue' : 'McpAccessValue')}
          title={translate('Access')}
        />
      </DescriptionList>

      <h3 className={styles.subheading}>{translate('McpToolsCount', { count: status.tools.length })}</h3>
      <ul aria-label={translate('McpTools')} className={styles.items}>
        {status.tools.map((tool) => (
          <li key={tool.name}>
            <span className={styles.toolName}>
              <code className={styles.name}>{tool.name}</code>
              {tool.readOnly ? null : <span className={styles.writeBadge}>{translate('McpWriteTool')}</span>}
            </span>
            <span className={styles.description}>{tool.description}</span>
          </li>
        ))}
      </ul>

      <h3 className={styles.subheading}>{translate('McpResourcesCount', { count: status.resources.length })}</h3>
      <ul aria-label={translate('McpResources')} className={styles.items}>
        {status.resources.map((resource) => (
          <li key={resource.uri}>
            <code className={styles.name}>{resource.uri}</code>
            <span className={styles.description}>{resource.description}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}
