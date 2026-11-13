import { useMemo } from 'react';

import type MaintenanceReport from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import ActiveFilter from './ActiveFilter';
import { filterPlatforms, type MaintenanceFilter } from './maintenanceFilters';
import styles from './MaintenanceIndex.module.css';
import PlatformRow from './PlatformRow';

interface PlatformsTabProps {
  report: MaintenanceReport;
  filter: MaintenanceFilter | null;
  onClearFilter: () => void;
}

/** "Platforms": what each machine runs (K3s, its operating system), the newer versions that exist and when support stops. */
export default function PlatformsTab({ report, filter, onClearFilter }: Readonly<PlatformsTabProps>) {
  const platforms = useMemo(() => filterPlatforms(report.platforms, filter), [report.platforms, filter]);

  return (
    <>
      <p className={styles.muted}>{translate('PlatformsHint')}</p>
      {filter ? (
        <ActiveFilter
          label={translate(`Filter_${filter}`)}
          onClear={onClearFilter}
          shown={platforms.length}
          total={report.platforms.length}
        />
      ) : null}
      {report.platforms.length === 0 ? <p className={styles.muted}>{translate('PlatformsEmptyHint')}</p> : null}
      {filter && platforms.length === 0 && report.platforms.length > 0 ? (
        <p className={styles.muted}>{translate('NoPlatformMatches')}</p>
      ) : null}
      {platforms.length > 0 ? (
        <ul aria-label={translate('Platforms')} className={styles.list}>
          {platforms.map((platform) => (
            <PlatformRow key={`${platform.environmentId}|${platform.machineId}|${platform.kind}`} platform={platform} />
          ))}
        </ul>
      ) : null}
    </>
  );
}
