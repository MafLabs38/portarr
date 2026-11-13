import { useMemo } from 'react';
import { useSearchParams } from 'react-router';

import { useApps } from 'Apps/useApps';
import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import * as icons from 'Helpers/Props/icons';
import type App from 'typings/App';
import translate from 'Utilities/String/translate';

import AttentionList from './AttentionList';
import CodeSourceNotices from './CodeSourceNotices';
import CodeTab from './CodeTab';
import ImagesTab from './ImagesTab';
import { activeFilter, codeTab, imagesTab, type MaintenanceTab, platformsTab } from './maintenanceFilters';
import MaintenanceFreshness from './MaintenanceFreshness';
import styles from './MaintenanceIndex.module.css';
import MaintenanceSummaryBar from './MaintenanceSummaryBar';
import MaintenanceTabs from './MaintenanceTabs';
import PlatformsTab from './PlatformsTab';
import useMaintenance from './useMaintenance';

/**
 * "What is vulnerable, outdated or late in my fleet?" (V4): what needs attention first, then two sub-tabs for
 * the parts of the fleet: the code the operator makes (repositories, deployed here or not), the images the
 * fleet runs and the platforms the machines run (K3s, their operating system). The summary figures and the attention lines filter the tabs (`?tab=images&filter=behind`, kept
 * in the URL). Portarr informs; the operator acts on GitHub and in the deployments (D-16).
 */
export default function MaintenanceIndex() {
  const maintenance = useMaintenance();
  const { data: appList } = useApps();
  const apps = useMemo(() => new Map<string, App>((appList ?? []).map((app) => [app.id, app])), [appList]);
  const [params, setParams] = useSearchParams();
  const tabParam = params.get('tab');
  const tab: MaintenanceTab = tabParam === imagesTab || tabParam === platformsTab ? tabParam : codeTab;
  const filter = activeFilter(tab, params.get('filter'));
  const report = maintenance.data;
  const source = report?.codeSource;
  const clearFilter = () => setParams(tab === codeTab ? {} : { tab }, { replace: false });

  return (
    <PageContent title={translate('Maintenance')}>
      <PageContentBody>
        <PageHeading scope={translate('Maintenance')} title={translate('MaintenanceTitle')} />

        {maintenance.isError ? <Alert kind="danger">{translate('ErrorLoadingMaintenance')}</Alert> : null}

        {report && source ? (
          <>
            <CodeSourceNotices source={source} />

            <MaintenanceFreshness refresh={report.refresh} source={source} />

            <MaintenanceSummaryBar filter={filter} summary={report.summary} tab={tab} />

            <section aria-labelledby="needs-attention" className={styles.section}>
              <h2 className={styles.heading} id="needs-attention">
                <icons.DEGRADED aria-hidden="true" size={18} strokeWidth={2.25} />
                {translate('NeedsAttention')}
              </h2>
              <AttentionList items={report.attention} />
            </section>

            <section className={styles.section}>
              <MaintenanceTabs
                onSelect={(id) => setParams(id === codeTab ? {} : { tab: id }, { replace: true })}
                selected={tab}
                tabs={[
                  { id: codeTab, label: translate('MyCode'), count: report.repositories.length },
                  { id: imagesTab, label: translate('DeployedImages'), count: report.images.length },
                  { id: platformsTab, label: translate('Platforms'), count: report.platforms.length },
                ]}
              >
                {tab === codeTab ? (
                  <CodeTab apps={apps} filter={filter} onClearFilter={clearFilter} report={report} source={source} />
                ) : null}
                {tab === imagesTab ? (
                  <ImagesTab apps={apps} filter={filter} onClearFilter={clearFilter} report={report} />
                ) : null}
                {tab === platformsTab ? (
                  <PlatformsTab filter={filter} onClearFilter={clearFilter} report={report} />
                ) : null}
              </MaintenanceTabs>
            </section>
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
