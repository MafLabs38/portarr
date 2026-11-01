import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './AssistantIndex.module.css';
import CatalogAuditCard from './CatalogAuditCard';
import ConnectCard from './ConnectCard';
import KnowledgePreview from './KnowledgePreview';
import McpServerCard from './McpServerCard';
import { useAssistantStatus, useKnowledge } from './useAssistant';

/**
 * "Ask your AI about the fleet" (V5): Portarr's MCP server, how to connect an assistant to it, and the knowledge
 * base the assistant reads, and the catalog edits made through the optional write tools (D-86). No tool acts on
 * the infrastructure: the assistant describes, the operator acts (D-31).
 */
export default function AssistantIndex() {
  const status = useAssistantStatus();
  const knowledge = useKnowledge(status.isSuccess);
  const data = status.data;
  const endpoint = data ? `${globalThis.location.origin}${data.path}` : '';

  return (
    <PageContent title={translate('Assistant')}>
      <PageContentBody>
        <PageHeading scope={translate('Assistant')} title={translate('AssistantTitle')} />
        <p className={styles.lead}>{translate('AssistantLead')}</p>

        {status.isError ? <Alert kind="danger">{translate('ErrorLoadingAssistant')}</Alert> : null}

        {data ? (
          <>
            {data.enabled ? null : <Alert kind="info">{translate('McpDisabledHint')}</Alert>}
            <div className={styles.cards}>
              <McpServerCard endpoint={endpoint} status={data} />
              <ConnectCard endpoint={endpoint} headerName={data.headerName} />
              <CatalogAuditCard status={data} />
            </div>

            <section aria-labelledby="knowledge-base" className={styles.knowledgeSection}>
              <h2 className={styles.heading} id="knowledge-base">
                <icons.KNOWLEDGE aria-hidden="true" size={18} strokeWidth={2.25} />
                {translate('KnowledgeBase')}
              </h2>
              {knowledge.isError ? <Alert kind="warning">{translate('ErrorLoadingKnowledge')}</Alert> : null}
              {knowledge.data ? <KnowledgePreview knowledge={knowledge.data} /> : null}
            </section>
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
