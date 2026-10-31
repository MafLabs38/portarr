import { activeFilter, dataSearch, filterAssets, groupAssets } from 'Data/dataFilters';

import { dataReport } from '__mocks__/server';

describe('dataFilters', () => {
  it('keeps the assets a figure counts', () => {
    const names = (filter: Parameters<typeof filterAssets>[1]) =>
      filterAssets(dataReport.assets, filter).map((a) => a.id);

    expect(names('at-risk')).toEqual(['shop-db', 'vault-data']);
    expect(names('protected')).toEqual(['sonarr-config']);
    expect(names('waiting')).toEqual(['swag-config']);
    expect(names('not-set-up')).toEqual([]);
    expect(names('offsite-gap')).toEqual(['vault-data', 'swag-config']);
    expect(names(null)).toHaveLength(4);
  });

  it('ignores a filter it does not know and builds the URL of the others', () => {
    expect(activeFilter('missing')).toBe('missing');
    expect(activeFilter('constructor')).toBeNull();
    expect(activeFilter(null)).toBeNull();
    expect(dataSearch('at-risk')).toBe('?filter=at-risk');
    expect(dataSearch()).toBe('?');
  });

  it('groups by importance then app, keeping the worst first', () => {
    const groups = groupAssets(dataReport.assets);

    expect(groups.map((g) => g.importance)).toEqual(['critical', 'important']);
    expect(groups[0]?.apps[0]?.assets.map((a) => a.id)).toEqual(['shop-db', 'vault-data', 'swag-config']);
  });
});
