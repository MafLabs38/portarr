import { expect, test } from '@playwright/test';

// V5 and V6: the Assistant page (MCP), and the chat panel opened from the header. No model is ever called.
test.describe('Assistant', { tag: '@smoke' }, () => {
  test('the page says whether the MCP server is on and what it offers', async ({ page }) => {
    await page.goto('/assistant');
    await expect(page.getByRole('heading', { level: 1, name: 'Ask your AI about the fleet' })).toBeVisible();

    const server = page.getByRole('region', { name: 'MCP server' });
    await expect(server.getByText('State', { exact: true })).toBeVisible();
    await expect(server.getByText(/^(On|Off)$/)).toBeVisible();
  });

  test('the chat panel opens from the header, takes a message, and closes', async ({ page }) => {
    await page.goto('/apps');

    await page.getByRole('button', { name: 'Chat' }).click();
    const panel = page.getByRole('complementary', { name: 'Chat' });
    await expect(panel).toBeVisible();
    await expect(panel.getByRole('heading', { level: 2, name: 'New chat' })).toBeVisible();
    await expect(panel.getByRole('combobox', { name: 'Agent' })).toBeVisible();

    // Nothing is sent: the button only becomes available once there is something to say.
    const send = panel.getByRole('button', { name: 'Send' });
    await expect(send).toBeDisabled();
    await panel.getByRole('textbox', { name: 'Message' }).fill('What needs my attention?');
    await expect(send).toBeEnabled();

    await panel.getByRole('button', { name: 'Close the chat' }).click();
    await expect(panel).toBeHidden();
  });
});
