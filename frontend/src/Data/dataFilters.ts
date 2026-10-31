import type { DataAsset, Importance } from 'typings/Data';

/** Filters of the Data page: what a summary tile counts (`?filter=at-risk`, kept in the URL). */
export const dataFilters = {
  protected: (a: DataAsset) => a.state === 'protected',
  'at-risk': (a: DataAsset) => a.state === 'late' || a.state === 'failed' || a.state === 'missing',
  late: (a: DataAsset) => a.state === 'late',
  waiting: (a: DataAsset) => a.state === 'waiting',
  'not-set-up': (a: DataAsset) => a.state === 'not-set-up',
  failed: (a: DataAsset) => a.state === 'failed',
  missing: (a: DataAsset) => a.state === 'missing',
  unknown: (a: DataAsset) => a.state === 'unknown',
  /** Critical data with no off-site copy that ever succeeded: the figure "x of y critical have one". */
  'offsite-gap': (a: DataAsset) => a.importance === 'critical' && a.offsite?.lastSuccessAt == null,
} as const satisfies Record<string, (asset: DataAsset) => boolean>;

export type DataFilter = keyof typeof dataFilters;

export function activeFilter(id: string | null): DataFilter | null {
  return id !== null && Object.hasOwn(dataFilters, id) ? (id as DataFilter) : null;
}

export function filterAssets(assets: DataAsset[], filter: DataFilter | null): DataAsset[] {
  return filter ? assets.filter(dataFilters[filter]) : assets;
}

/** The query string that shows the page, optionally filtered. */
export function dataSearch(filter?: DataFilter): string {
  return filter ? `?filter=${filter}` : '?';
}

export const importanceOrder: Importance[] = ['critical', 'important', 'comfort'];

export interface AppGroup {
  appId: string;
  assets: DataAsset[];
}

export interface ImportanceGroup {
  importance: Importance;
  apps: AppGroup[];
}

/**
 * Assets grouped by importance, then by app. The API sends the assets at risk first: groups keep the order of
 * their first (worst) asset, so what needs attention stays on top of each level.
 */
export function groupAssets(assets: DataAsset[]): ImportanceGroup[] {
  return importanceOrder
    .map((importance) => {
      const apps = new Map<string, AppGroup>();
      for (const asset of assets.filter((a) => a.importance === importance)) {
        const group = apps.get(asset.appId) ?? { appId: asset.appId, assets: [] };
        group.assets.push(asset);
        apps.set(asset.appId, group);
      }
      return { importance, apps: [...apps.values()] };
    })
    .filter((group) => group.apps.length > 0);
}
