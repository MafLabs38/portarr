import { expect, test } from '@playwright/test';

test.describe('Launcher and workloads', { tag: '@smoke' }, () => {
  test('apps are grouped by stack with statuses resolved from their containers', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/apps$/);
    const demo = page.getByRole('region', { name: 'Demo' });
    await expect(demo.getByRole('article', { name: 'Web' }).getByText('Healthy')).toBeVisible({ timeout: 30_000 });
    await expect(demo.getByRole('article', { name: 'Broken job' }).getByText('Down')).toBeVisible();
    await expect(demo.getByText('Degraded', { exact: true })).toBeVisible();
    await expect(demo.getByRole('link', { name: 'Web' })).toHaveAttribute('href', 'http://localhost:8080/');
    await expect(page.getByRole('region', { name: 'Other apps' }).getByRole('article', { name: 'Docs' })).toBeVisible();
  });

  test('a container shows its usage and live logs', async ({ page, request }) => {
    // Generate a log line in the web container.
    await request.get('http://localhost:8080/portarr-e2e-probe').catch(() => undefined);

    await page.goto('/workloads');
    await page.getByRole('link', { name: 'e2e-web' }).click();

    await expect(page).toHaveURL(/\/workloads\/e2e\/e2e-web$/);
    await expect(page.getByText(/vCPU$/)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('● Live')).toBeVisible();
    await expect(page.getByRole('log')).toContainText('portarr-e2e-probe', { timeout: 15_000 });
  });
});
