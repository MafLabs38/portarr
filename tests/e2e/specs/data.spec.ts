import { expect, test } from '@playwright/test';

// V3: the Data page. It must hold on any catalog, even one that follows no data: the figures are links that filter
// the list, and the filter is kept in the URL.
test.describe('Data', { tag: '@smoke' }, () => {
  test('the summary separates what is proven, pending and at risk, and its figures filter the list', async ({
    page,
  }) => {
    await page.goto('/data');
    await expect(page.getByRole('heading', { level: 1, name: 'Data and backups' })).toBeVisible();

    // Waiting for a first run and a tool that is not set up are not "at risk" and have their own figures.
    await expect(page.getByRole('list', { name: 'Coverage', exact: true })).toBeVisible();
    const notProven = page.getByRole('list', { name: 'Not proven yet' });
    await expect(notProven.getByRole('listitem', { name: /^Waiting for first run: \d+$/ })).toBeVisible();
    await expect(notProven.getByRole('listitem', { name: /^Not set up: \d+$/ })).toBeVisible();
    await expect(notProven.getByRole('listitem', { name: /^Unknown: \d+$/ })).toBeVisible();
    await expect(page.getByRole('list', { name: 'At risk' })).toBeVisible();

    await page.getByRole('link', { name: /Critical with off-site copy/ }).click();
    await expect(page).toHaveURL(/\/data\?filter=offsite-gap$/);
    const clear = page.getByRole('button', { name: 'Clear the filter: Critical without an off-site copy' });
    await expect(clear).toBeVisible();
    await expect(page.getByText(/^\d+ of \d+ shown$/)).toBeVisible();

    await clear.click();
    await expect(page).toHaveURL(/\/data\??$/);
    await expect(clear).toHaveCount(0);
  });

  test('each followed asset shows its local and its off-site copy with a state', async ({ page }) => {
    await page.goto('/data');
    const assets = page.locator('main li[aria-label]').filter({ has: page.getByRole('group', { name: 'Local copy' }) });
    test.skip((await assets.count()) === 0, 'This catalog follows no data.');

    const first = assets.first();
    await expect(first.getByRole('group', { name: /^Local copy/ })).toBeVisible();
    await expect(first.getByRole('group', { name: /^Off-site copy/ })).toBeVisible();
    await expect(
      first.getByText(/Protected|Waiting for first run|Not set up|Unknown|Late|Failed|Missing/).first(),
    ).toBeVisible();
  });
});
