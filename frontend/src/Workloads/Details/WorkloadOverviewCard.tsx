import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import StatusLabel from 'Components/Status/StatusLabel';
import type Workload from 'typings/Workload';
import translate from 'Utilities/String/translate';

/** What a workload is: status, image, state, where it runs and which apps it belongs to. */
export default function WorkloadOverviewCard({ workload }: Readonly<{ workload: Workload }>) {
  return (
    <Card title={translate('Overview')}>
      <DescriptionList>
        <DescriptionListItem data={<StatusLabel showReason status={workload.status} />} title={translate('Status')} />
        <DescriptionListItem data={workload.image} monospace title={translate('Image')} />
        <DescriptionListItem data={workload.statusText} title={translate('State')} />
        {workload.namespace ? (
          <DescriptionListItem data={workload.namespace} title={translate('Namespace')} />
        ) : (
          <DescriptionListItem data={workload.composeProject ?? '—'} title={translate('Project')} />
        )}
        {workload.node ? <DescriptionListItem data={workload.node} title={translate('Node')} /> : null}
        <DescriptionListItem data={workload.appIds.join(', ') || '—'} title={translate('App')} />
        {workload.namespace ? null : (
          <DescriptionListItem data={workload.id.slice(0, 12)} monospace title={translate('ContainerId')} />
        )}
      </DescriptionList>
    </Card>
  );
}
