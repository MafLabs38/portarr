import ActiveFilter from 'Maintenance/ActiveFilter';
import { useMemo } from 'react';
import { useSearchParams } from 'react-router';

import { useApps } from 'Apps/useApps';
import Alert from 'Components/Alert/Alert';
import AppIcon from 'Components/AppIcon/AppIcon';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import * as icons from 'Helpers/Props/icons';
import type App from 'typings/App';
import translate from 'Utilities/String/translate';

import AssetRow from './AssetRow';
import { activeFilter, filterAssets, groupAssets } from './dataFilters';
import DataFreshness from './DataFreshness';
import styles from './DataIndex.module.css';
import DataSummaryBar from './DataSummaryBar';
import useData from './useData';

/**
 * "If everything crashes, can I get my state back?" (V3): the data the operator chose to follow, grouped by
 * importance and app, at-risk first. Each has a local and an off-site copy to be proved by a backup tool
 * (Backrest, K8up, a CronJob); Portarr compares that policy with what the tools report and runs no backup
 * (D-43). The summary figures filter the list (`?filter=at-risk`, kept in the URL).
 */
export default function DataIndex() {
  const data = useData();
  const { data: appList } = useApps();
  const apps = useMemo(() => new Map<string, App>((appList ?? []).map((app) => [app.id, app])), [appList]);
  const [params, setParams] = useSearchParams();
  const filter = activeFilter(params.get('filter'));
  const report = data.data;
  const assets = useMemo(() => filterAssets(report?.assets ?? [], filter), [report?.assets, filter]);
  const groups = useMemo(() => groupAssets(assets), [assets]);

  return (
    <PageContent title={translate('Data')}>
      <PageContentBody>
        <PageHeading scope={translate('Data')} title={translate('DataAndBackups')} />

        {data.isError ? <Alert kind="danger">{translate('ErrorLoadingData')}</Alert> : null}

        {report ? (
          <>
            <DataFreshness sources={report.sources} />
            <DataSummaryBar filter={filter} summary={report.summary} />

            {report.summary.followed === 0 ? <p className={styles.muted}>{translate('DataEmptyHint')}</p> : null}
            {filter ? (
              <ActiveFilter
                label={translate(`DataFilter_${filter}`)}
                onClear={() => setParams({}, { replace: false })}
                shown={assets.length}
                total={report.assets.length}
              />
            ) : null}
            {filter && assets.length === 0 ? <p className={styles.muted}>{translate('NoDataMatches')}</p> : null}

            {groups.map((group) => (
              <section
                aria-labelledby={`importance-${group.importance}`}
                className={styles.section}
                key={group.importance}
              >
                <h2 className={styles.heading} id={`importance-${group.importance}`}>
                  <icons.DATA aria-hidden="true" size={18} strokeWidth={2.25} />
                  {translate(`Importance_${group.importance}`)}
                  <span className={styles.count}>{group.apps.reduce((sum, app) => sum + app.assets.length, 0)}</span>
                </h2>
                {group.apps.map((appGroup) => {
                  const app = apps.get(appGroup.appId);
                  const name = app?.name ?? appGroup.appId;
                  return (
                    <div className={styles.group} key={appGroup.appId}>
                      <h3 className={styles.groupHeading}>
                        <AppIcon icon={app?.icon ?? null} name={name} size={18} />
                        <span className={styles.appName}>{name}</span>
                      </h3>
                      <ul aria-label={name} className={styles.list}>
                        {appGroup.assets.map((asset) => (
                          <AssetRow asset={asset} key={asset.id} />
                        ))}
                      </ul>
                    </div>
                  );
                })}
              </section>
            ))}
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}
