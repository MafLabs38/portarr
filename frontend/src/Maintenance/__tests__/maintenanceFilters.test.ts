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

  it('keeps the repositories each figure of the summary counts', () => {
    expect(names(filterRepositories(repositories, 'severe'))).toEqual(['example/shop']);
    expect(names(filterRepositories(repositories, 'ci-failing'))).toEqual(['example/shop']);
    expect(names(filterRepositories(repositories, 'dependabot-off'))).toEqual(['example/desktop']);
    expect(names(filterRepositories(repositories, 'code-scanning-off'))).toEqual(['example/shop', 'example/desktop']);
    expect(names(filterRepositories(repositories, 'moderate'))).toEqual(['example/shop']);
    expect(filterRepositories(repositories, 'secrets')).toEqual([]);
  });

  it('keeps the images each figure of the summary counts', () => {
    expect(references(filterImages(images, 'behind'))).toEqual(['postgres:16-alpine']);
    expect(references(filterImages(images, 'major'))).toEqual(['postgres:16-alpine']);
    expect(filterImages(images, 'behind-minor')).toEqual([]);
    expect(references(filterImages(images, 'cannot-tell'))).toEqual(['grafana/grafana:latest']);
    expect(filterImages(images, 'up-to-date')).toEqual([]);
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
