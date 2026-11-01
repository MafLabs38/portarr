import { expect, test } from '@playwright/test';

// V4: Maintenance. The figures are links that filter the page; it must hold on a catalog without a code source.

// The page is given a report of its own: a registry on the internet cannot decide whether this test passes. The real
// pipeline (catalog, registries, report) is proved by the integration tests; what a browser adds is the links and the URL.
const behindImage = {
  reference: 'postgres:16-alpine',
  registry: 'docker.io',
  repository: 'library/postgres',
  tag: '16-alpine',
  status: 'behind',
  current: '16-alpine',
  latest: '18-alpine',
  change: 'major',
  currentAt: null,
  latestAt: '2026-01-01T00:00:00Z',
  reason: null,
  fromLabel: false,
  users: [{ environmentId: 'server-1', environmentName: 'Server 1', name: 'db', namespace: null, appIds: [] }],
  repositoryFullName: null,
};

const report = {
  summary: {
    repositories: 0,
    deployedRepositories: 0,
    critical: 0,
    high: 0,
    medium: 0,
    low: 0,
    qualityAlerts: 0,
    secretAlerts: 0,
    dependabotOff: 0,
    codeScanningOff: 0,
    ciFailing: 0,
    dependabotPullRequests: 0,
    images: 2,
    imagesUpToDate: 1,
    imagesBehind: 1,
    imagesMajor: 1,
    imagesFloating: 0,
    imagesUnknown: 0,
  },
  attention: [],
  codeSource: { configured: false, owners: [], scannedAt: null, error: null },
  repositories: [],
  images: [
    behindImage,
    {
      ...behindImage,
      reference: 'redis:7',
      repository: 'library/redis',
      tag: '7',
      status: 'up-to-date',
      current: '7',
      latest: null,
      change: null,
      latestAt: null,
    },
  ],
  refresh: {
    gitHubAt: null,
    gitHubNextAt: null,
    registriesAt: '2026-01-01T12:00:00Z',
    refreshing: false,
    availableAt: null,
  },
};

test.describe('Maintenance', { tag: '@smoke' }, () => {
  test('the figures of the deployed images filter the list and the filter can be cleared', async ({ page }) => {
    await page.route('**/api/v1/maintenance', (route) => route.fulfill({ json: report }));
    await page.goto('/maintenance');
    await expect(page.getByRole('heading', { level: 1, name: 'What needs attention' })).toBeVisible();

    const images = page.getByRole('list', { name: 'Deployed images' });
    await expect(images).toBeVisible();
    await images.getByRole('link', { name: /Behind$/ }).click();

    await expect(page).toHaveURL(/filter=behind/);
    const clear = page.getByRole('button', { name: /^Clear the filter/ });
    await expect(clear).toBeVisible();
    await expect(page.getByText('1 of 2 shown')).toBeVisible();

    await clear.click();
    await expect(page).not.toHaveURL(/filter=/);
    await expect(clear).toHaveCount(0);
  });

  test('a refresh can be asked for, once a minute', async ({ page }) => {
    await page.goto('/maintenance');

    const refresh = page.getByRole('button', { name: 'Refresh' });
    await expect(refresh).toBeVisible();
    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/v1/maintenance/refresh')),
      refresh.click(),
    ]);

    // The click did something: Portarr answered that it will scan (or, within a minute of another, that it will not).
    expect([202, 429]).toContain(response.status());
  });
});
