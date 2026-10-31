import { defineConfig, devices } from '@playwright/test';

import { storageState } from './global-setup';

// Runs against a started Portarr (the e2e stack in CI, or a local instance).
const baseURL = process.env.PORTARR_URL ?? 'http://localhost:8484';

export default defineConfig({
  testDir: './specs',
  outputDir: '../../test-results',
  // One shared instance: specs that change settings must not interleave.
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  globalSetup: './global-setup.ts',
  reporter: [['list'], ['html', { outputFolder: '../../playwright-report', open: 'never' }]],
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  ],
});
