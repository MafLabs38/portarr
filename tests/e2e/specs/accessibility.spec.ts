import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

// Every page of the portal, once, with axe: the pages of V2 to V7 (the launcher and the workload page are checked
// in launcher-and-workloads.spec.ts, on the stack's own containers).
const pages: [string, string][] = [
  ['/data', 'Data and backups'],
  ['/maintenance', 'What needs attention'],
  ['/assistant', 'Ask your AI about the fleet'],
  ['/system/status', 'Status'],
  ['/settings/users', 'Users & Auth'],
  ['/account/profile', 'Profile'],
  ['/account/api-keys', 'API keys'],
];

test.describe('Accessibility', { tag: '@smoke' }, () => {
  for (const [path, heading] of pages) {
    test(`${path} has no accessibility violation`, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible({ timeout: 15_000 });

      const results = await new AxeBuilder({ page }).analyze();

      expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
    });
  }
});
