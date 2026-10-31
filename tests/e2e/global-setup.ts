import { request } from '@playwright/test';
import { fileURLToPath } from 'node:url';

// The password of the administrator the e2e stack creates on its first run (V7): a throwaway for a throwaway stack.
const { E2E_ADMIN_USER: username = 'e2e-admin', E2E_ADMIN_PASSWORD: pass = 'e2e-throwaway-passphrase' } = process.env;

/** The administrator the specs sign in as (the stack creates it unattended, see stack/docker-compose.yml). */
export const credentials = { username, password: pass };

export const storageState = fileURLToPath(new URL('.auth/admin.json', import.meta.url));

/** Creates the first administrator (or signs in when the instance already has one) and keeps the session for every spec. */
export default async function globalSetup() {
  const baseURL = process.env.PORTARR_URL ?? 'http://localhost:8484';
  const context = await request.newContext({ baseURL, extraHTTPHeaders: { 'X-Requested-With': 'portarr' } });
  const status = (await (await context.get('/api/v1/auth/status')).json()) as { setupRequired: boolean };
  const response = await context.post(status.setupRequired ? '/api/v1/auth/setup' : '/api/v1/auth/login', {
    data: { username, password: pass },
  });
  if (!response.ok()) {
    throw new Error(`Unable to sign in to the e2e instance (${response.status()}).`);
  }
  await context.storageState({ path: storageState });
  await context.dispose();
}
