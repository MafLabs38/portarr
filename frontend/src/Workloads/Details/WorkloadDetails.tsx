import { useParams } from 'react-router-dom';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import { useWorkload, useWorkloadUsage } from '../useWorkloads';
import LogViewer from './LogViewer';
import styles from './WorkloadDetails.module.css';

export default function WorkloadDetails() {
  const { environmentId = '', name = '' } = useParams();
  const { data: workload, isError: notFound } = useWorkload(environmentId, name);
  const usage = useWorkloadUsage(environmentId, name, Boolean(workload) && workload?.appIds.length !== 0);
  const base = `/workloads/${encodeURIComponent(environmentId)}/${encodeURIComponent(name)}`;

  return (
    <PageContent title={name}>
      <PageContentBody>
        <PageHeading scope={environmentId} title={name} />

        {notFound ? <Alert kind="warning">{translate('WorkloadNotFound')}</Alert> : null}

        {workload ? (
          <div className={styles.layout}>
            <Card title={translate('Overview')}>
              <DescriptionList>
                <DescriptionListItem
                  data={<StatusLabel showReason status={workload.status} />}
                  title={translate('Status')}
                />
                <DescriptionListItem data={workload.image} monospace title={translate('Image')} />
                <DescriptionListItem data={workload.statusText} title={translate('State')} />
                <DescriptionListItem data={workload.composeProject ?? '—'} title={translate('Project')} />
                <DescriptionListItem data={workload.appIds.join(', ') || '—'} title={translate('App')} />
                <DescriptionListItem data={workload.id.slice(0, 12)} monospace title={translate('ContainerId')} />
              </DescriptionList>
            </Card>

            <Card title={translate('Resources')}>
              {usage.data ? (
                <DescriptionList>
                  <DescriptionListItem data={formatCores(usage.data.cpuCores)} monospace title={translate('Cpu')} />
                  <DescriptionListItem
                    data={
                      usage.data.memoryLimitBytes
                        ? translate('MemoryOfLimit', {
                            used: formatBytes(usage.data.memoryBytes),
                            limit: formatBytes(usage.data.memoryLimitBytes),
                          })
                        : formatBytes(usage.data.memoryBytes)
                    }
                    monospace
                    title={translate('Memory')}
                  />
                  <DescriptionListItem data={translate('ViaDocker')} title={translate('Source')} />
                </DescriptionList>
              ) : (
                <p className={styles.muted}>{translate(usage.isError ? 'UsageUnavailable' : 'Loading')}</p>
              )}
            </Card>
          </div>
        ) : null}

        {workload && workload.appIds.length > 0 ? <LogViewer path={`${base}/logs?tail=200`} /> : null}
        {workload && workload.appIds.length === 0 ? (
          <Alert kind="info">{translate('LogsOnlyForCatalogApps')}</Alert>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
