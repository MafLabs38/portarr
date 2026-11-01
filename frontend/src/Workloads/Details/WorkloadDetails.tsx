import { useParams } from 'react-router';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import translate from 'Utilities/String/translate';

import { useWorkload, useWorkloadUsage } from '../useWorkloads';
import LogViewer from './LogViewer';
import styles from './WorkloadDetails.module.css';
import WorkloadOverviewCard from './WorkloadOverviewCard';
import WorkloadResourcesCard from './WorkloadResourcesCard';

export default function WorkloadDetails() {
  const { environmentId = '', name = '' } = useParams();
  const { data: workload, isError: notFound } = useWorkload(environmentId, name);
  const declared = workload?.appIds.length !== 0;
  const usage = useWorkloadUsage(environmentId, name, Boolean(workload?.supportsUsage) && declared);
  const logs = Boolean(workload?.supportsLogs) && declared;
  const base = `/workloads/${encodeURIComponent(environmentId)}/${encodeURIComponent(name)}`;

  return (
    <PageContent title={name}>
      <PageContentBody>
        <PageHeading scope={environmentId} title={name} />

        {notFound ? <Alert kind="warning">{translate('WorkloadNotFound')}</Alert> : null}

        {workload ? (
          <div className={styles.layout}>
            <WorkloadOverviewCard workload={workload} />
            <WorkloadResourcesCard usage={usage} workload={workload} />
          </div>
        ) : null}

        {logs ? <LogViewer path={`${base}/logs?tail=200`} /> : null}
        {workload?.supportsUsage && workload.appIds.length === 0 ? (
          <Alert kind="info">{translate('LogsOnlyForCatalogApps')}</Alert>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
