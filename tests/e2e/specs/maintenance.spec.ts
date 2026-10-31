import { expect, test } from '@playwright/test';

// V4: Maintenance. The figures are links that filter the page; it must hold on a catalog without a code source.
test.describe('Maintenance', { tag: '@smoke' }, () => {
  test('the figures of the deployed images filter the list and the filter can be cleared', async ({ page }) => {
    await page.goto('/maintenance');
    await expect(page.getByRole('heading', { level: 1, name: 'What needs attention' })).toBeVisible();

    const images = page.getByRole('list', { name: 'Deployed images' });
    await expect(images).toBeVisible({ timeout: 30_000 });
    await images.getByRole('link', { name: /Behind$/ }).click();

    await expect(page).toHaveURL(/filter=behind/);
    const clear = page.getByRole('button', { name: /^Clear the filter/ });
    await expect(clear).toBeVisible();
    await expect(page.getByText(/^\d+ of \d+ shown$/)).toBeVisible();

    await clear.click();
    await expect(page).not.toHaveURL(/filter=/);
    await expect(clear).toHaveCount(0);
  });

  test('a refresh can be asked for, once a minute', async ({ page }) => {
    await page.goto('/maintenance');

    const refresh = page.getByRole('button', { name: 'Refresh' });
    await expect(refresh).toBeVisible();
    await refresh.click();
    await expect(page.getByText(/updated|Refreshing|next in/i).first()).toBeVisible();
  });
});
