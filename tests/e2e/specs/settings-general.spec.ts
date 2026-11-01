import { expect, test } from '@playwright/test';

// D-91: the portal's identity is a setting, on Settings > General.
test.describe('Settings, General', { tag: '@smoke' }, () => {
  test('the Identity card shows the name the menu carries, and nothing is saved until it changes', async ({ page }) => {
    await page.goto('/settings/general');

    const identity = page.getByRole('region', { name: 'Identity' });
    await expect(identity).toBeVisible();
    const name = identity.getByRole('textbox', { name: 'Name', exact: true });
    await expect(name).not.toHaveValue('');
    const shown = await name.inputValue();
    // The menu carries the name as text; its logo is an image named after it only once one is uploaded.
    await expect(page.getByRole('complementary').getByText(shown, { exact: true })).toBeVisible();
    await expect(identity.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
});
