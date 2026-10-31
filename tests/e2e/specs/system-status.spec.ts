import { expect, test } from '@playwright/test';

test.describe('System status', { tag: '@smoke' }, () => {
  test('the status page shows the running version', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('navigation', { name: 'Main menu' }).getByRole('link', { name: 'System' }).click();

    await expect(page).toHaveURL(/\/system\/status$/);
    await expect(page).toHaveTitle('Status - Portarr');
    await expect(page.getByRole('heading', { level: 1, name: 'Status' })).toBeVisible();

    const about = page.getByRole('region', { name: 'About' });
    await expect(about.getByText('Portarr', { exact: true })).toBeVisible();
    await expect(about.getByText(/^\.NET /)).toBeVisible();
    await expect(about.getByText(/^(SQLite|PostgreSQL) \d/)).toBeVisible();
  });

  test('the Identity card shows the name the menu carries, and nothing is saved until it changes', async ({ page }) => {
    await page.goto('/system/status');

    const identity = page.getByRole('region', { name: 'Identity' });
    await expect(identity).toBeVisible();
    const name = identity.getByRole('textbox', { name: 'Name', exact: true });
    await expect(name).not.toHaveValue('');
    const shown = await name.inputValue();
    // The menu carries the name as text; its logo is an image named after it only once one is uploaded.
    await expect(page.getByRole('complementary').getByText(shown, { exact: true })).toBeVisible();
    await expect(identity.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  test('liveness and readiness probes answer', async ({ request }) => {
    for (const path of ['/health/live', '/health/ready']) {
      const response = await request.get(path);

      expect(response.status(), path).toBe(200);
      expect(await response.text(), path).toBe('Healthy');
    }
  });

  test('unknown API routes return 404 instead of the frontend', async ({ request }) => {
    const response = await request.get('/api/v1/unknown');

    expect(response.status()).toBe(404);
  });
});
