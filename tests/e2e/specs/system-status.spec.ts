import { expect, test } from '@playwright/test';

test.describe('System status', { tag: '@smoke' }, () => {
  test('home redirects to the status page, which shows the running version', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/system\/status$/);
    await expect(page).toHaveTitle('Status - Portarr');
    await expect(page.getByRole('heading', { level: 1, name: 'Status' })).toBeVisible();

    const about = page.getByRole('region', { name: 'About' });
    await expect(about.getByText('Portarr', { exact: true })).toBeVisible();
    await expect(about.getByText(/^\.NET /)).toBeVisible();
  });

  test('unknown API routes return 404 instead of the frontend', async ({ request }) => {
    const response = await request.get('/api/v1/unknown');

    expect(response.status()).toBe(404);
  });
});
