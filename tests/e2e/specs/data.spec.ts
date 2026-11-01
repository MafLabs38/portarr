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

  test('the asset the stack follows shows its local copy (made by hand, undated) and its off-site copy (nothing produces it)', async ({
    page,
  }) => {
    // tests/e2e/stack/catalog.yaml follows one asset: a critical "Web content" with a manual local copy and no off-site source.
    await page.goto('/data');
    const asset = page.getByRole('listitem', { name: /^Web content/ });
    await expect(asset).toBeVisible();

    const local = asset.getByRole('group', { name: /^Local copy/ });
    await expect(local).toBeVisible();
    await expect(local.getByText('Made by hand')).toBeVisible();

    const offsite = asset.getByRole('group', { name: /^Off-site copy/ });
    await expect(offsite).toBeVisible();
    await expect(offsite.getByText('declared, and nothing produces it yet')).toBeVisible();
    await expect(offsite.getByText('Missing')).toBeVisible();
  });
});
