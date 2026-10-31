import { useMemo } from 'react';

import type App from 'typings/App';
import type MaintenanceReport from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import ActiveFilter from './ActiveFilter';
import ImageRow from './ImageRow';
import { filterImages, type MaintenanceFilter } from './maintenanceFilters';
import styles from './MaintenanceIndex.module.css';

interface ImagesTabProps {
  report: MaintenanceReport;
  filter: MaintenanceFilter | null;
  apps: Map<string, App>;
  onClearFilter: () => void;
}

/** "Deployed images": every image the fleet runs against its registry, filtered by a summary figure. */
export default function ImagesTab({ report, filter, apps, onClearFilter }: Readonly<ImagesTabProps>) {
  const images = useMemo(() => filterImages(report.images, filter), [report.images, filter]);

  return (
    <>
      <p className={styles.muted}>{translate('ImagesHint')}</p>
      {filter ? (
        <ActiveFilter
          label={translate(`Filter_${filter}`)}
          onClear={onClearFilter}
          shown={images.length}
          total={report.images.length}
        />
      ) : null}
      {report.images.length === 0 ? <p className={styles.muted}>{translate('ImagesEmptyHint')}</p> : null}
      {filter && images.length === 0 && report.images.length > 0 ? (
        <p className={styles.muted}>{translate('NoImageMatches')}</p>
      ) : null}
      {images.length > 0 ? (
        <ul aria-label={translate('DeployedImages')} className={styles.list}>
          {images.map((image) => (
            <ImageRow
              apps={apps}
              image={image}
              key={`${image.reference}|${image.current ?? ''}|${image.status}|${image.users[0]?.environmentId ?? ''}|${image.users[0]?.name ?? ''}`}
            />
          ))}
        </ul>
      ) : null}
    </>
  );
}
