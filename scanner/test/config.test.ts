import assert from 'node:assert/strict';
import test from 'node:test';

import { loadConfig } from '../src/config.ts';

const base = { PORTARR_SCANNER_SECRET: 's'.repeat(40) };

test('the defaults: one scan of ten minutes, a rescan after a day, forgotten after a week', () => {
  const config = loadConfig(base);

  assert.equal(config.port, 8489);
  assert.equal(config.scanTimeoutMs, 10 * 60_000);
  assert.equal(config.rescanAfterMs, 24 * 3_600_000);
  assert.equal(config.forgetAfterMs, 7 * 86_400_000);
  assert.equal(config.dataDir, '/data');
});

test('the secret is required and must be long enough', () => {
  assert.throws(() => loadConfig({}), /PORTARR_SCANNER_SECRET must be at least 32/);
  assert.throws(() => loadConfig({ PORTARR_SCANNER_SECRET: 'short' }), /at least 32/);
});

test('the numbers are checked', () => {
  assert.equal(loadConfig({ ...base, PORTARR_SCANNER_TIMEOUT_MINUTES: '30' }).scanTimeoutMs, 30 * 60_000);
  assert.throws(() => loadConfig({ ...base, PORTARR_SCANNER_PORT: '0' }), /PORTARR_SCANNER_PORT/);
  assert.throws(
    () => loadConfig({ ...base, PORTARR_SCANNER_TIMEOUT_MINUTES: 'soon' }),
    /PORTARR_SCANNER_TIMEOUT_MINUTES/,
  );
});
