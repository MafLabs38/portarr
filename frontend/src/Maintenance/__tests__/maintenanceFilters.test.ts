import {
  activeFilter,
  attentionSearch,
  filterImages,
  filterRepositories,
  maintenanceSearch,
} from 'Maintenance/maintenanceFilters';

import { maintenanceReport } from '__mocks__/server';

const names = (items: { fullName: string }[]) => items.map((item) => item.fullName);
const references = (items: { reference: string }[]) => items.map((item) => item.reference);

describe('maintenanceFilters', () => {
  const { repositories, images } = maintenanceReport;

  it.each([
    ['severe', ['example/shop']],
    ['ci-failing', ['example/shop']],
    ['dependabot-off', ['example/desktop']],
    ['code-scanning-off', ['example/shop', 'example/desktop']],
    ['moderate', ['example/shop']],
    ['secrets', []],
  ] as const)('keeps the repositories the "%s" figure of the summary counts', (filter, expected) => {
    expect(names(filterRepositories(repositories, filter))).toEqual(expected);
  });

  it.each([
    ['behind', ['postgres:16-alpine']],
    ['major', ['postgres:16-alpine']],
    ['behind-minor', []],
    ['cannot-tell', ['grafana/grafana:latest']],
    ['up-to-date', []],
  ] as const)('keeps the images the "%s" figure of the summary counts', (filter, expected) => {
    expect(references(filterImages(images, filter))).toEqual(expected);
  });

  it('keeps nothing for a figure the report has no match for, instead of everything', () => {
    // 'secrets' has no repository in the mock: an empty list, not the unfiltered one.
    expect(filterRepositories(repositories, 'secrets')).not.toBe(repositories);
    expect(filterRepositories([], 'severe')).toEqual([]);
  });

  it('shows everything when there is no filter', () => {
    expect(filterRepositories(repositories, null)).toBe(repositories);
    expect(filterImages(images, null)).toBe(images);
  });

  it('ignores a filter that is unknown or belongs to the other tab', () => {
    expect(activeFilter('code', 'dependabot-off')).toBe('dependabot-off');
    expect(activeFilter('code', 'major')).toBeNull();
    expect(activeFilter('images', 'dependabot-off')).toBeNull();
    expect(activeFilter('images', 'toString')).toBeNull();
    expect(activeFilter('code', 'nope')).toBeNull();
    expect(activeFilter('code', null)).toBeNull();
  });

  it('builds the query string of a tab and a filter', () => {
    expect(maintenanceSearch('code')).toBe('?');
    expect(maintenanceSearch('images')).toBe('?tab=images');
    expect(maintenanceSearch('images', 'behind')).toBe('?tab=images&filter=behind');
    expect(maintenanceSearch('code', 'ci-failing')).toBe('?filter=ci-failing');
  });

  it('maps the findings of the attention list to a filter, except the GitHub error', () => {
    expect(attentionSearch('severe-alerts')).toBe('?filter=severe');
    expect(attentionSearch('image-major')).toBe('?tab=images&filter=major');
    expect(attentionSearch('images-behind')).toBe('?tab=images&filter=behind-minor');
    expect(attentionSearch('github-error')).toBeNull();
  });
});
