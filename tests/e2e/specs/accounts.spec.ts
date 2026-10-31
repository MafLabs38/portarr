import { expect, test } from '@playwright/test';

import { credentials } from '../global-setup';

// V7: local accounts, sign-in and the Users & Auth settings.
test.describe('Accounts', { tag: '@smoke' }, () => {
  test.describe('signed out', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('a visitor must sign in; a wrong password is refused, the right one opens the app, and logging out closes it', async ({
      page,
    }) => {
      await page.goto('/apps');
      await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
      await expect(page.getByRole('navigation', { name: 'Main menu' })).toHaveCount(0);

      await page.getByLabel('User name').fill(credentials.username);
      await page.getByLabel('Password').fill(`${credentials.password}-wrong`);
      await page.getByRole('button', { name: 'Sign in' }).click();
      await expect(page.getByText(/Invalid username or password/)).toBeVisible();

      await page.getByLabel('Password').fill(credentials.password);
      await page.getByRole('button', { name: 'Sign in' }).click();
      await expect(page.getByRole('heading', { level: 1, name: 'Apps' })).toBeVisible();

      await page.getByRole('button', { name: /^Account menu of / }).click();
      await page.getByRole('menuitem', { name: 'Log out' }).click();
      await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
      await page.goto('/system/status');
      await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    });
  });

  test('an administrator creates a user, then deletes it', async ({ page }) => {
    const username = `e2e-user-${Date.now()}`;
    await page.goto('/settings/users');
    await expect(page.getByRole('heading', { level: 1, name: 'Users & Auth' })).toBeVisible();

    await page.getByRole('button', { name: 'Add user' }).click();
    const form = page.getByRole('form', { name: 'Add user' });
    await form.getByLabel('User name').fill(username);
    await form.getByLabel('Display name').fill('E2E temporary user');
    await form.getByLabel('Role').selectOption('Observer');
    await form.getByLabel('Password').fill('a-long-throwaway-passphrase');
    await form.getByRole('button', { name: 'Save' }).click();

    const row = page.getByRole('row', { name: new RegExp(username) });
    await expect(row).toBeVisible();
    await expect(row.getByRole('cell', { name: 'Observer' })).toBeVisible();

    // Deleting asks for a confirmation.
    await row.getByRole('button', { name: `Delete user ${username}` }).click();
    await row.getByRole('button', { name: 'Confirm delete' }).click();
    await expect(row).toHaveCount(0);
  });
});
