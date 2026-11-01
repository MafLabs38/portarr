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

    // The age moves with the clock and the samples: a live card shows more than one age and is never "stale".
    const seen = new Set<string>();
    const agesSeen = async () => {
      const text =
        (await card
          .getByText(/^(Live|Stale) · /)
          .first()
          .textContent({ timeout: 1_000 })) ?? '';
      expect(text).not.toMatch(/^Stale/);
      seen.add(text);
      return seen.size;
    };
    await expect.poll(agesSeen, { timeout: 20_000 }).toBeGreaterThan(1);
  });

  test('a card states how much CPU and memory the machine uses', async ({ page }) => {
    await page.goto('/infrastructure');
    const card = page.getByRole('article').first();

    await expect(card.getByRole('region', { name: 'CPU' })).toBeVisible({ timeout: 30_000 });
    await expect(card.getByRole('region', { name: 'Memory' })).toBeVisible();
  });
});
