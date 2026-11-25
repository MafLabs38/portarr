import assert from 'node:assert/strict';
import { chmod, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { reasonOf, ScanFailure, TrivyCli } from '../src/trivy.ts';

/** A shell script that stands for Trivy: it records its arguments and answers as the first argument asks. */
async function fakeTrivy(script: string): Promise<{ command: string; dir: string }> {
  const dir = await mkdtemp(join(tmpdir(), 'portarr-trivy-'));
  const command = join(dir, 'trivy');
  await writeFile(command, `#!/bin/sh\necho "$@" >> "${dir}/calls"\n${script}\n`);
  await chmod(command, 0o755);
  return { command, dir };
}

test('the error text of Trivy becomes a short code', () => {
  assert.equal(reasonOf('FATAL ... MANIFEST_UNKNOWN: manifest unknown'), 'not-found');
  assert.equal(reasonOf('FATAL ... UNAUTHORIZED: authentication required'), 'unauthorized');
  assert.equal(reasonOf('denied: requested access to the resource is denied'), 'unauthorized');
  assert.equal(reasonOf('dial tcp: lookup registry.example: no such host'), 'not-found');
  assert.equal(reasonOf('context deadline exceeded'), 'timeout');
  assert.equal(reasonOf('something else'), 'error');
});

test('the image is one argument after --, with the cache in the data folder, and the report is read', async () => {
  const { command, dir } = await fakeTrivy(`echo '{"Results":[{"Vulnerabilities":[]}]}'`);
  const cli = new TrivyCli(command, join(dir, 'cache'), 60_000, false);

  const report = await cli.scan('app:1');

  assert.deepEqual(report, { Results: [{ Vulnerabilities: [] }] });
  const call = (await readFile(join(dir, 'calls'), 'utf8')).trim();
  assert.match(
    call,
    /^image --image-src remote --format json --quiet --no-progress --scanners vuln --timeout 60s --cache-dir .*\/cache -- app:1$/,
  );
});

test('the scan runs at a low priority by default', async () => {
  const { command, dir } = await fakeTrivy(`echo '{"Results":[]}'`);

  const report = await new TrivyCli(command, join(dir, 'cache'), 60_000).scan('app:1');

  assert.deepEqual(report, { Results: [] });
});

test('a failed scan says why, from what Trivy printed', async () => {
  const { command, dir } = await fakeTrivy(`echo 'FATAL MANIFEST_UNKNOWN' >&2; exit 1`);

  await assert.rejects(new TrivyCli(command, dir, 60_000, false).scan('gone:1'), (error: unknown) => {
    assert.ok(error instanceof ScanFailure);
    assert.equal(error.reason, 'not-found');
    return true;
  });
});

test('an answer that is not JSON, a program that is missing and a scan that never ends are errors, not hangs', async () => {
  const notJson = await fakeTrivy(`echo 'not json'`);
  await assert.rejects(new TrivyCli(notJson.command, notJson.dir, 60_000, false).scan('a:1'), { reason: 'error' });

  await assert.rejects(new TrivyCli(join(notJson.dir, 'missing'), notJson.dir, 60_000, false).scan('a:1'), {
    reason: 'error',
  });

  const slow = await fakeTrivy('sleep 30');
  await assert.rejects(new TrivyCli(slow.command, slow.dir, 300, false, 0).scan('a:1'), { reason: 'timeout' });
});

test('clean drops the scan cache, and the database is read from the version command', async () => {
  const { command, dir } = await fakeTrivy(
    `if [ "$1" = version ]; then echo '{"Version":"0.67.2","VulnerabilityDB":{"Version":2,"UpdatedAt":"2026-11-25T01:02:03Z"}}'; fi`,
  );
  const cli = new TrivyCli(command, join(dir, 'cache'), 60_000, false);

  await cli.clean();
  const database = await cli.database();

  assert.deepEqual(database, { version: '2', updatedAt: '2026-11-25T01:02:03.000Z' });
  assert.match(await readFile(join(dir, 'calls'), 'utf8'), /clean --scan-cache --cache-dir/);
});

test('a database that cannot be read is an empty answer', async () => {
  const { command, dir } = await fakeTrivy(`echo 'oops'`);

  assert.deepEqual(await new TrivyCli(command, dir, 60_000, false).database(), {});
});
