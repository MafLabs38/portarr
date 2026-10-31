import { expect, test } from '@playwright/test';

test.describe('Logging and metrics', { tag: '@smoke' }, () => {
  test('the log level changed in System > Status is kept after a reload', async ({ page, request }) => {
    await page.goto('/system/status');
    const select = page.getByRole('combobox', { name: 'Log level' });
    await expect(select).toHaveValue(/.+/);

    await select.selectOption('Debug');
    await expect(select).toHaveValue('Debug');
    await page.reload();

    await expect(page.getByRole('combobox', { name: 'Log level' })).toHaveValue('Debug');

    // Leave the instance as it was for the other tests.
    await request.put('/api/v1/system/logging', {
      data: { level: 'Information' },
      headers: { 'X-Requested-With': 'portarr' },
    });
  });

  test('Prometheus metrics are exposed', async ({ request }) => {
    const response = await request.get('/metrics');

    expect(response.status()).toBe(200);
    expect(await response.text()).toContain('portarr_build_info{version=');
  });
});
