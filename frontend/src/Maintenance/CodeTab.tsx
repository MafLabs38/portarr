import { useMemo } from 'react';

import type App from 'typings/App';
import type MaintenanceReport from 'typings/Maintenance';
import type { CodeSource } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import ActiveFilter from './ActiveFilter';
import { filterRepositories, type MaintenanceFilter } from './maintenanceFilters';
import styles from './MaintenanceIndex.module.css';
import RepositoryGroup from './RepositoryGroup';

interface CodeTabProps {
  report: MaintenanceReport;
  source: CodeSource;
  filter: MaintenanceFilter | null;
  apps: Map<string, App>;
  onClearFilter: () => void;
}

function scanHint(source: CodeSource): string {
  return source.scannedAt
    ? translate('CodeScannedHint', { owners: source.owners.join(', '), age: formatAge(source.scannedAt) })
    : translate('CodeNotScannedYet');
}

/** "My code": the operator's repositories, those running in the fleet first, filtered by a summary figure. */
export default function CodeTab({ report, source, filter, apps, onClearFilter }: Readonly<CodeTabProps>) {
  const repositories = useMemo(() => filterRepositories(report.repositories, filter), [report.repositories, filter]);
  const deployed = useMemo(() => repositories.filter((r) => r.deployed), [repositories]);
  const elsewhere = useMemo(() => repositories.filter((r) => !r.deployed), [repositories]);

  return (
    <>
      <p className={styles.muted}>{scanHint(source)}</p>
      {filter ? (
        <ActiveFilter
          label={translate(`Filter_${filter}`)}
          onClear={onClearFilter}
          shown={repositories.length}
          total={report.repositories.length}
        />
      ) : null}
      {filter && repositories.length === 0 ? <p className={styles.muted}>{translate('NoRepositoryMatches')}</p> : null}
      <RepositoryGroup apps={apps} repositories={deployed} title={translate('RunningInTheFleet')} />
      <RepositoryGroup apps={apps} repositories={elsewhere} title={translate('NotDeployedHereGroup')} />
    </>
  );
}
