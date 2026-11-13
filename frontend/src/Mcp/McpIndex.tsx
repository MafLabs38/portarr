import MaintenanceTabs from 'Maintenance/MaintenanceTabs';
import { useSearchParams } from 'react-router';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import translate from 'Utilities/String/translate';

import McpServersTab from './McpServersTab';
import McpWorkstationsTab from './McpWorkstationsTab';
import { useMcpServers, useMcpWorkstations } from './useMcp';

const serversTab = 'servers';
const workstationsTab = 'workstations';

/**
 * "Which MCP servers do I use, are they alive, and where do I want them?": the servers the catalog declares, each remote one with
 * its health and since when, and the workstations with the servers wanted on each. Portarr describes, the assistant on a machine
 * installs (D-110). The tab is kept in the URL (`?tab=workstations`).
 */
export default function McpIndex() {
  const servers = useMcpServers();
  const workstations = useMcpWorkstations();
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab');
  const tab = requested === workstationsTab ? requested : serversTab;

  return (
    <PageContent title={translate('MCP')}>
      <PageContentBody>
        <PageHeading scope={translate('MCP')} title={translate('McpTitle')} />

        {servers.isError ? <Alert kind="danger">{translate('ErrorLoadingMcp')}</Alert> : null}

        <MaintenanceTabs
          onSelect={(id) => setParams(id === serversTab ? {} : { tab: id }, { replace: true })}
          selected={tab}
          tabs={[
            { id: serversTab, label: translate('McpServersTab'), count: servers.data?.length },
            { id: workstationsTab, label: translate('McpWorkstationsTab'), count: workstations.data?.length },
          ]}
        >
          {tab === serversTab ? <McpServersTab servers={servers.data} /> : null}
          {tab === workstationsTab ? (
            <McpWorkstationsTab
              isError={workstations.isError}
              servers={servers.data}
              workstations={workstations.data}
            />
          ) : null}
        </MaintenanceTabs>
      </PageContentBody>
    </PageContent>
  );
}
