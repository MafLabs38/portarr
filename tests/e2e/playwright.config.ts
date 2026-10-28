import { defineConfig, devices } from '@playwright/test';

// Runs against a started Portarr (the e2e stack in CI, or a local instance).
const baseURL = process.env.PORTARR_URL ?? 'http://localhost:8484';

export default defineConfig({
  testDir: './specs',
  outputDir: '../../test-results',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { outputFolder: '../../playwright-report', open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  ],
});
