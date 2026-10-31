import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import type AssistantStatus from 'typings/Assistant';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './AssistantIndex.module.css';
import { useCatalogAudit } from './useAssistant';

function editsState(status: AssistantStatus) {
  if (!status.catalogWritable) {
    return { text: translate('CatalogEditsOff'), className: styles.off };
  }
  return status.writeTools
    ? { text: translate('CatalogEditsOn'), className: styles.on }
    : { text: translate('CatalogEditsApiOnly'), className: styles.on };
}

/**
 * Whether an assistant may edit the catalog (D-85, D-86), and the latest edits from the audit log: what, through
 * which channel (api or mcp) and when.
 */
export default function CatalogAuditCard({ status }: Readonly<{ status: AssistantStatus }>) {
  const writable = status.catalogWritable;
  const audit = useCatalogAudit(writable);
  const state = editsState(status);

  return (
    <Card title={translate('CatalogEdits')}>
      <DescriptionList>
        <DescriptionListItem data={<span className={state.className}>{state.text}</span>} title={translate('State')} />
      </DescriptionList>

      {audit.isError ? <Alert kind="warning">{translate('ErrorLoadingCatalogAudit')}</Alert> : null}
      {writable && audit.data ? (
        <ul aria-label={translate('CatalogEdits')} className={styles.items}>
          {audit.data.length === 0 ? <li className={styles.description}>{translate('CatalogEditsNone')}</li> : null}
          {audit.data.map((entry) => (
            <li key={`${entry.at}-${entry.section}-${entry.id}`}>
              <code className={styles.name}>
                {translate('CatalogEditLine', { operation: entry.operation, section: entry.section, id: entry.id })}
              </code>
              <span className={styles.description}>{entry.changes.join(' · ')}</span>
              <span className={styles.meta}>
                {translate('CatalogEditMeta', {
                  channel: entry.via?.startsWith('chat')
                    ? translate('CatalogEditViaChat', { channel: entry.channel })
                    : entry.channel,
                  age: formatAge(entry.at),
                })}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
    </Card>
  );
}
