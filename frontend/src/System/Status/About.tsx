import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import formatUptime from 'Utilities/Number/formatUptime';
import translate from 'Utilities/String/translate';

import useSystemStatus from './useSystemStatus';

export default function About() {
  const { data, isPending, isError } = useSystemStatus();

  return (
    <Card title={translate('About')}>
      {isPending ? <p>{translate('Loading')}</p> : null}
      {isError ? <p role="alert">{translate('ErrorLoadingSystemStatus')}</p> : null}
      {data ? (
        <DescriptionList>
          <DescriptionListItem data={data.appName} title={translate('AppName')} />
          <DescriptionListItem data={data.version} monospace title={translate('Version')} />
          <DescriptionListItem data={data.runtimeVersion} monospace title={translate('Runtime')} />
          <DescriptionListItem data={data.osDescription} monospace title={translate('OperatingSystem')} />
          <DescriptionListItem
            data={new Date(data.startTime).toLocaleString()}
            monospace
            title={translate('StartTime')}
          />
          <DescriptionListItem data={formatUptime(data.uptimeSeconds)} title={translate('Uptime')} />
        </DescriptionList>
      ) : null}
    </Card>
  );
}
