import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import test from 'node:test';

import { ScanFailure } from '../src/trivy.ts';
import { harness, hours, report, vulnerability } from './fakes.ts';

test('an image named is queued, scanned and answered with its counts', async () => {
  const { scanner } = await harness();

  assert.equal(scanner.request(['app:1']), 1);
  assert.equal(scanner.results().images[0].state, 'scanning');
  await scanner.idle();

  const [image] = scanner.results().images;
  assert.equal(image.state, 'scanned');
  assert.equal(image.scannedAt, '2026-11-25T10:00:00.000Z');
  assert.equal(image.counts.high, 1);
  assert.equal(image.fixable.high, 1);
  assert.equal(image.findings[0].id, 'CVE-2026-0001');
  assert.deepEqual(scanner.results().database, { version: '2', updatedAt: '2026-11-25T00:00:00.000Z' });
});

test('an image scanned less than 24 hours ago is not scanned again, a day later it is', async () => {
  const { scanner, trivy, clock } = await harness();
  scanner.request(['app:1']);
  await scanner.idle();

  clock.now += hours(23);
  assert.equal(scanner.request(['app:1']), 0);
  clock.now += hours(2);
  assert.equal(scanner.request(['app:1']), 1);
  await scanner.idle();

  assert.deepEqual(trivy.scanned, ['app:1', 'app:1']);
});

test('the same image named twice in a request, or while it is queued, is queued once', async () => {
  const { scanner, trivy } = await harness();
  trivy.gate = new Promise(() => undefined);

  assert.equal(scanner.request(['a:1', 'a:1', 'b:1']), 2);
  assert.equal(scanner.request(['a:1', 'b:1']), 0);
  assert.deepEqual(
    scanner.results().images.map((image) => [image.ref, image.state]),
    [
      ['a:1', 'scanning'],
      ['b:1', 'queued'],
    ],
  );
});

test('images are scanned one at a time, new ones first, then the ones scanned longest ago', async () => {
  const { scanner, trivy, clock } = await harness();
  scanner.request(['old:1']);
  await scanner.idle();
  clock.now += hours(1);
  scanner.request(['newer:1']);
  await scanner.idle();
  trivy.scanned.length = 0;

  clock.now += hours(30);
  scanner.request(['newer:1', 'old:1', 'brand-new:1']);
  await scanner.idle();

  assert.deepEqual(trivy.scanned, ['brand-new:1', 'old:1', 'newer:1']);
});

test('a scan that fails says why with a short code, and the others go on', async () => {
  const { scanner, trivy } = await harness();
  trivy.answers.set('private:1', new ScanFailure('unauthorized'));
  trivy.answers.set('local:1', new ScanFailure('not-found'));
  trivy.answers.set('scratch:1', { SchemaVersion: 2 });
  trivy.answers.set('ok:1', report(vulnerability({ Severity: 'LOW' })));

  scanner.request(['private:1', 'local:1', 'scratch:1', 'ok:1']);
  await scanner.idle();

  const states = Object.fromEntries(
    scanner.results().images.map((image) => [image.ref, `${image.state}:${image.reason ?? ''}`]),
  );
  assert.deepEqual(states, {
    'private:1': 'failed:unauthorized',
    'local:1': 'failed:not-found',
    'scratch:1': 'failed:unsupported',
    'ok:1': 'scanned:',
  });
});

test('a failure that may pass (a timeout) is tried again after an hour, one that will not (not found) after a day', async () => {
  const { scanner, trivy, clock } = await harness();
  trivy.answers.set('slow:1', new ScanFailure('timeout'));
  trivy.answers.set('gone:1', new ScanFailure('not-found'));
  scanner.request(['slow:1', 'gone:1']);
  await scanner.idle();

  clock.now += hours(2);
  assert.equal(scanner.request(['slow:1', 'gone:1']), 1);
  await scanner.idle();
  assert.deepEqual(trivy.scanned, ['slow:1', 'gone:1', 'slow:1']);

  clock.now += hours(23);
  assert.equal(scanner.request(['gone:1']), 1);
});

test('an error that is not a scan failure is an error, and the cache is cleaned after every scan', async () => {
  const { scanner, trivy } = await harness();
  trivy.scan = () => Promise.reject(new Error('boom'));

  scanner.request(['a:1', 'b:1']);
  await scanner.idle();

  assert.deepEqual(
    scanner.results().images.map((image) => image.reason),
    ['error', 'error'],
  );
  assert.equal(trivy.cleaned, 2);
});

test('a clean-up that fails does not lose the result', async () => {
  const { scanner, trivy } = await harness();
  trivy.clean = () => Promise.reject(new Error('disk'));

  scanner.request(['a:1']);
  await scanner.idle();

  assert.equal(scanner.results().images[0].state, 'scanned');
});

test('results survive a restart, and an image nobody names for a week is forgotten', async () => {
  const first = await harness();
  first.scanner.request(['kept:1', 'dropped:1']);
  await first.scanner.idle();
  const file = JSON.parse(await readFile(join(first.dir, 'results.json'), 'utf8')) as { records: unknown[] };
  assert.equal(file.records.length, 2);

  const second = await harness(first.dir);
  second.clock.now = first.clock.now + hours(24 * 6);
  assert.deepEqual(
    second.scanner.results().images.map((image) => [image.ref, image.state]),
    [
      ['dropped:1', 'scanned'],
      ['kept:1', 'scanned'],
    ],
  );
  second.scanner.request(['kept:1']);
  await second.scanner.idle();
  second.clock.now += hours(24 * 2);
  second.scanner.request(['kept:1']);
  await second.scanner.idle();

  assert.deepEqual(
    second.scanner.results().images.map((image) => image.ref),
    ['kept:1'],
  );
});

test('a results file that cannot be read starts empty', async () => {
  const { dir } = await harness();
  const { writeFile } = await import('node:fs/promises');
  await writeFile(join(dir, 'results.json'), '{ not json');

  const again = await harness(dir);

  assert.deepEqual(again.scanner.results().images, []);
});
