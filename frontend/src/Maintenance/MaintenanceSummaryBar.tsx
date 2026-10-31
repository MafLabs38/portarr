import SummaryTile, { type Tile } from 'Data/SummaryTile';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { MaintenanceSummary } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import {
  codeTab,
  imagesTab,
  type MaintenanceFilter,
  maintenanceSearch,
  type MaintenanceTab,
} from './maintenanceFilters';
import styles from './MaintenanceSummaryBar.module.css';

function alarm(count: number, kind: kinds.Kind): kinds.Kind {
  return count > 0 ? kind : kinds.DISABLED;
}

interface MaintenanceSummaryBarProps {
  summary: MaintenanceSummary;
  /** The tab and filter shown: their tile is marked as the active one. */
  tab: MaintenanceTab;
  filter: MaintenanceFilter | null;
}

/**
 * The fleet's maintenance in two lines of figures: the code the operator makes (alerts by severity, security
 * features turned off, failing CI) and the images the fleet runs (behind, major updates, cannot tell). Each
 * figure is a link that shows the items it counts (the filter is kept in the URL).
 */
export default function MaintenanceSummaryBar({ summary, tab, filter }: Readonly<MaintenanceSummaryBarProps>) {
  const severe = summary.critical + summary.high;
  const link = (target: MaintenanceTab, id?: MaintenanceFilter): Pick<Tile, 'to' | 'active'> => ({
    to: maintenanceSearch(target, id),
    active: tab === target && (filter ?? undefined) === id,
  });
  const code: Tile[] = [
    {
      key: 'repositories',
      ...link(codeTab),
      value: `${summary.repositories}`,
      label: translate('RepositoriesDeployed', { count: summary.deployedRepositories }),
      kind: kinds.DEFAULT,
      icon: icons.CODE_REPOSITORY,
    },
    {
      key: 'severe',
      ...link(codeTab, 'severe'),
      value: `${severe}`,
      label: translate('CriticalHighAlerts'),
      kind: alarm(severe, kinds.DANGER),
      icon: icons.VULNERABILITY,
    },
    {
      key: 'moderate',
      ...link(codeTab, 'moderate'),
      value: `${summary.medium + summary.low}`,
      label: translate('MediumLowAlerts'),
      kind: alarm(summary.medium + summary.low, kinds.WARNING),
      icon: icons.VULNERABILITY,
    },
    {
      key: 'dependabot-off',
      ...link(codeTab, 'dependabot-off'),
      value: translate('CountOf', { count: summary.dependabotOff, total: summary.repositories }),
      label: translate('DependabotOff'),
      kind: alarm(summary.dependabotOff, kinds.WARNING),
      icon: icons.SCANNING_OFF,
    },
    {
      key: 'ci-failing',
      ...link(codeTab, 'ci-failing'),
      value: `${summary.ciFailing}`,
      label: translate('CiFailing'),
      kind: alarm(summary.ciFailing, kinds.DANGER),
      icon: icons.CI,
    },
  ];
  const images: Tile[] = [
    {
      key: 'images',
      ...link(imagesTab),
      value: `${summary.images}`,
      label: translate('RunningImages'),
      kind: kinds.DEFAULT,
      icon: icons.IMAGE,
    },
    {
      key: 'up-to-date',
      ...link(imagesTab, 'up-to-date'),
      value: `${summary.imagesUpToDate}`,
      label: translate('ImageStatus_up-to-date'),
      kind: alarm(summary.imagesUpToDate, kinds.SUCCESS),
      icon: icons.HEALTHY,
    },
    {
      key: 'behind',
      ...link(imagesTab, 'behind'),
      value: `${summary.imagesBehind}`,
      label: translate('ImagesBehind'),
      kind: alarm(summary.imagesBehind, kinds.WARNING),
      icon: icons.UPDATE,
    },
    {
      key: 'major',
      ...link(imagesTab, 'major'),
      value: `${summary.imagesMajor}`,
      label: translate('MajorUpdates'),
      kind: alarm(summary.imagesMajor, kinds.WARNING),
      icon: icons.UPDATE,
    },
    {
      key: 'cannot-tell',
      ...link(imagesTab, 'cannot-tell'),
      value: `${summary.imagesFloating + summary.imagesUnknown}`,
      label: translate('CannotTell'),
      kind: kinds.DISABLED,
      icon: icons.UNKNOWN,
    },
  ];

  return (
    <div className={styles.bar}>
      <ul aria-label={translate('MyCode')} className={styles.group}>
        {code.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
      <ul aria-label={translate('DeployedImages')} className={styles.group}>
        {images.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
    </div>
  );
}
