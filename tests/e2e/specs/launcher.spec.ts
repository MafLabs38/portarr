import { expect, test } from '@playwright/test';

// V1: the Apps launcher, on any catalog: apps with their status and a way to open them.
test.describe('Apps launcher', { tag: '@smoke' }, () => {
  test('apps are listed with a status, and the way to open them can be chosen', async ({ page }) => {
    await page.goto('/apps');
    await expect(page.getByRole('heading', { level: 1, name: 'Apps' })).toBeVisible();

    const app = page.getByRole('article').first();
    await expect(app).toBeVisible({ timeout: 15_000 });
    await expect(app.getByRole('link').first()).toHaveAttribute('href', /.+/);

    const opening = page.getByRole('group', { name: 'Open apps with' });
    await expect(opening.getByRole('radio', { name: 'Public', exact: true })).toBeVisible();
    await opening.getByRole('radio', { name: 'Public', exact: true }).check();
    await expect(opening.getByRole('radio', { name: 'Public', exact: true })).toBeChecked();
    await opening.getByRole('radio', { name: /^Auto/ }).check();
    await expect(opening.getByRole('radio', { name: /^Auto/ })).toBeChecked();
  });

  test('the menu leads to every page of the portal', async ({ page }) => {
    await page.goto('/apps');

    const menu = page.getByRole('navigation', { name: 'Main menu' });
    for (const [name, url] of [
      ['Infrastructure', /\/infrastructure$/],
      ['Data', /\/data$/],
      ['Maintenance', /\/maintenance$/],
      ['System', /\/system\/status$/],
      ['Apps', /\/apps$/],
    ] as const) {
      await menu.getByRole('link', { name }).click();
      await expect(page).toHaveURL(url);
    }
  });
});
