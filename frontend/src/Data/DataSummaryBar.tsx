import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { DataSummary } from 'typings/Data';
import translate from 'Utilities/String/translate';

import { type DataFilter, dataSearch } from './dataFilters';
import styles from './DataSummaryBar.module.css';
import SummaryTile, { type Tile } from './SummaryTile';

interface DataSummaryBarProps {
  summary: DataSummary;
  /** The filter applied: its tile is marked as the active one. */
  filter: DataFilter | null;
}

function alarm(count: number, kind: kinds.Kind): kinds.Kind {
  return count > 0 ? kind : kinds.DISABLED;
}

function offsiteKind(summary: DataSummary): kinds.Kind {
  if (summary.criticalAssets === 0) {
    return kinds.DISABLED;
  }
  if (summary.criticalOffsite === summary.criticalAssets) {
    return kinds.SUCCESS;
  }
  // Only jobs that have not run yet are missing from the count: pending, not a gap.
  return summary.criticalOffsite + summary.criticalOffsitePending === summary.criticalAssets
    ? kinds.INFO
    : kinds.DANGER;
}

/**
 * The answer to "if everything crashes, can I get my state back?" in one line: the data followed, what is
 * protected, what is at risk (late, failed, missing), what is not proven yet (waiting for a first run, tool not
 * set up, cannot be told) and how many critical assets have an off-site copy. Each figure is a link that shows the assets it counts (the filter is kept in the URL).
 */
export default function DataSummaryBar({ summary, filter }: Readonly<DataSummaryBarProps>) {
  const link = (id?: DataFilter): Pick<Tile, 'to' | 'active'> => ({
    to: dataSearch(id),
    active: (filter ?? undefined) === id,
  });
  const overview: Tile[] = [
    {
      key: 'followed',
      ...link(),
      value: `${summary.followed}`,
      label: translate('DataFollowed'),
      kind: kinds.DEFAULT,
      icon: icons.DATA,
    },
    {
      key: 'protected',
      ...link('protected'),
      value: `${summary.protected}`,
      label: translate('Coverage_protected'),
      kind: kinds.SUCCESS,
      icon: icons.PROTECTED,
    },
    {
      key: 'at-risk',
      ...link('at-risk'),
      value: `${summary.atRisk}`,
      label: translate('AtRisk'),
      kind: alarm(summary.atRisk, kinds.DANGER),
      icon: icons.NOT_COVERED,
    },
  ];
  const unproven: Tile[] = [
    {
      key: 'waiting',
      ...link('waiting'),
      value: `${summary.waiting}`,
      label: translate('Coverage_waiting'),
      kind: summary.waiting > 0 ? kinds.INFO : kinds.DISABLED,
      icon: icons.WAITING,
    },
    {
      key: 'not-set-up',
      ...link('not-set-up'),
      value: `${summary.notSetUp}`,
      label: translate('Coverage_not-set-up'),
      kind: alarm(summary.notSetUp, kinds.PRIMARY),
      icon: icons.NOT_SET_UP,
    },
    {
      key: 'unknown',
      ...link('unknown'),
      value: `${summary.unknown}`,
      label: translate('Coverage_unknown'),
      kind: kinds.DISABLED,
      icon: icons.UNKNOWN,
    },
  ];
  const risks: Tile[] = [
    {
      key: 'late',
      ...link('late'),
      value: `${summary.late}`,
      label: translate('Coverage_late'),
      kind: alarm(summary.late, kinds.WARNING),
      icon: icons.LATE,
    },
    {
      key: 'failed',
      ...link('failed'),
      value: `${summary.failed}`,
      label: translate('Coverage_failed'),
      kind: alarm(summary.failed, kinds.DANGER),
      icon: icons.DOWN,
    },
    {
      key: 'missing',
      ...link('missing'),
      value: `${summary.missing}`,
      label: translate('Coverage_missing'),
      kind: alarm(summary.missing, kinds.DANGER),
      icon: icons.NOT_COVERED,
    },
  ];
  const offsite: Tile[] = [
    {
      key: 'offsite',
      ...link('offsite-gap'),
      value: translate('CountOf', { count: summary.criticalOffsite, total: summary.criticalAssets }),
      label: translate('CriticalOffsite'),
      detail:
        summary.criticalOffsitePending > 0
          ? translate('PendingFirstRun', { count: summary.criticalOffsitePending })
          : undefined,
      kind: offsiteKind(summary),
      icon: icons.OFFSITE_COPY,
    },
  ];

  return (
    <div className={styles.bar}>
      <ul aria-label={translate('Coverage')} className={styles.group}>
        {overview.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
      <ul aria-label={translate('NotProvenYet')} className={styles.group}>
        {unproven.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
      <ul aria-label={translate('AtRisk')} className={styles.group}>
        {risks.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
      <ul aria-label={translate('OffsiteCoverage')} className={styles.group}>
        {offsite.map((tile) => (
          <SummaryTile key={tile.key} tile={tile} />
        ))}
      </ul>
    </div>
  );
}
