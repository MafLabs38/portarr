import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import type Workload from 'typings/Workload';
import type { WorkloadUsage } from 'typings/Workload';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCores from 'Utilities/Number/formatCores';
import translate from 'Utilities/String/translate';

import styles from './WorkloadDetails.module.css';

interface WorkloadResourcesCardProps {
  workload: Workload;
  usage: { data?: WorkloadUsage | null; isError: boolean };
}

function memoryText(usage: WorkloadUsage): string {
  return usage.memoryLimitBytes
    ? translate('MemoryOfLimit', { used: formatBytes(usage.memoryBytes), limit: formatBytes(usage.memoryLimitBytes) })
    : formatBytes(usage.memoryBytes);
}

function UsageList({ usage, viaMetricsServer }: Readonly<{ usage: WorkloadUsage; viaMetricsServer: boolean }>) {
  return (
    <DescriptionList>
      <DescriptionListItem data={formatCores(usage.cpuCores)} monospace title={translate('Cpu')} />
      <DescriptionListItem data={memoryText(usage)} monospace title={translate('Memory')} />
      <DescriptionListItem
        data={translate(viaMetricsServer ? 'ViaMetricsServer' : 'ViaDocker')}
        title={translate('Source')}
      />
    </DescriptionList>
  );
}

/** What a workload consumes now, or why it cannot be told (the connector does not measure it, or not yet). */
export default function WorkloadResourcesCard({ workload, usage }: Readonly<WorkloadResourcesCardProps>) {
  let content;
  if (!workload.supportsUsage) {
    content = <p className={styles.muted}>{translate('DetailsNotSupported')}</p>;
  } else if (usage.data) {
    content = <UsageList usage={usage.data} viaMetricsServer={Boolean(workload.namespace)} />;
  } else {
    content = <p className={styles.muted}>{translate(usage.isError ? 'UsageUnavailable' : 'Loading')}</p>;
  }

  return <Card title={translate('Resources')}>{content}</Card>;
}
