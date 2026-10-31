import { expect, test } from '@playwright/test';

// V5: the Assistant page: the state of the MCP server and what it offers.
test.describe('Assistant', { tag: '@smoke' }, () => {
  test('the page says whether the MCP server is on and what it offers', async ({ page }) => {
    await page.goto('/assistant');
    await expect(page.getByRole('heading', { level: 1, name: 'Ask your AI about the fleet' })).toBeVisible();

    const server = page.getByRole('region', { name: 'MCP server' });
    await expect(server.getByText('State', { exact: true })).toBeVisible();
    await expect(server.getByText(/^(On|Off)$/)).toBeVisible();
  });
});
