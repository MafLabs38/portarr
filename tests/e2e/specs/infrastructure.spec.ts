import { expect, test } from '@playwright/test';

// V2: the live Infrastructure page, one card per machine.
test.describe('Infrastructure', { tag: '@smoke' }, () => {
  test('a machine card is live: its sample ages, then a new one replaces it', async ({ page }) => {
    await page.goto('/infrastructure');
    await expect(page.getByRole('heading', { level: 1, name: 'Machines' })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Environments' })).toBeVisible();

    const card = page.getByRole('article').first();
    await expect(card).toBeVisible({ timeout: 30_000 });
    const age = card.getByText(/^Live · \d+ s ago$/);
    await expect(age).toBeVisible({ timeout: 30_000 });

    // The age ticks every second and the next sample resets it: it climbs, then a lower value shows the reset.
    let highest = 0;
    const sawAReset = async () => {
      const seconds = Number(/\d+/.exec((await age.textContent()) ?? '')?.[0]);
      if (Number.isNaN(seconds)) {
        return false;
      }

      highest = Math.max(highest, seconds);
      return seconds < highest;
    };
    await expect.poll(sawAReset, { timeout: 20_000 }).toBe(true);
    await expect(card.getByText(/^Stale/)).toHaveCount(0);
  });

  test('a card states how much CPU and memory the machine uses', async ({ page }) => {
    await page.goto('/infrastructure');
    const card = page.getByRole('article').first();

    await expect(card.getByRole('region', { name: 'CPU' })).toBeVisible({ timeout: 30_000 });
    await expect(card.getByRole('region', { name: 'Memory' })).toBeVisible();
  });
});
