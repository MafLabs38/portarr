import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('Launcher and workloads', { tag: '@smoke' }, () => {
  test('apps are grouped by stack with container state and healthcheck', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/apps$/);
    const demo = page.getByRole('region', { name: 'Demo' });
    const web = demo.getByRole('article', { name: 'Web' });
    await expect(web.getByText(/^Up /)).toBeVisible({ timeout: 15_000 });
    await expect(web.getByText('no healthcheck')).toBeVisible();
    // CI creates e2e-broken, which exits with code 1.
    await expect(demo.getByRole('article', { name: 'Broken job' }).getByText(/^Exited \(1\)/)).toBeVisible();
    await expect(demo.getByText('Degraded', { exact: true })).toBeVisible();
    await expect(demo.getByRole('link', { name: 'Web' })).toHaveAttribute('href', 'http://localhost:8080/');
  });

  test('a container page follows new log lines live', async ({ page, request }) => {
    await page.goto('/workloads');
    await page.getByRole('link', { name: 'e2e-web' }).click();

    await expect(page).toHaveURL(/\/workloads\/e2e\/e2e-web$/);
    await expect(page.getByText(/vCPU$/)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText('● Live')).toBeVisible();

    // Sent once the stream is open: the line can only arrive through live follow, not the tail.
    const probe = `portarr-e2e-probe-${Date.now()}`;
    expect((await request.get(`http://localhost:8080/${probe}`)).status()).toBe(404);
    await expect(page.getByRole('log')).toContainText(probe, { timeout: 5_000 });
  });

  const pages: [string, string][] = [
    ['/apps', 'article'],
    ['/workloads', 'table'],
    ['/workloads/e2e/e2e-web', 'log'],
  ];
  for (const [path, role] of pages) {
    test(`${path} has no accessibility violation`, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole(role as 'article' | 'table' | 'log').first()).toBeVisible({ timeout: 15_000 });

      const results = await new AxeBuilder({ page }).analyze();

      expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
    });
  }
});
