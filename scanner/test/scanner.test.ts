import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import test from 'node:test';

import { ScanFailure } from '../src/trivy.ts';
import { harness, hours, images, releases, report, vulnerability } from './fakes.ts';

test('an image named is queued, scanned and answered with its counts', async () => {
  const { scanner } = await harness();

  assert.equal(scanner.request(images('app:1')).queued, 1);
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
  scanner.request(images('app:1'));
  await scanner.idle();

  clock.now += hours(23);
  assert.equal(scanner.request(images('app:1')).queued, 0);
  clock.now += hours(2);
  assert.equal(scanner.request(images('app:1')).queued, 1);
  await scanner.idle();

  assert.deepEqual(trivy.scanned, ['app:1', 'app:1']);
});

test('the same image named twice in a request, or while it is queued, is queued once', async () => {
  const { scanner, trivy } = await harness();
  trivy.gate = new Promise(() => undefined);

  assert.equal(scanner.request(images('a:1', 'a:1', 'b:1')).queued, 2);
  assert.equal(scanner.request(images('a:1', 'b:1')).queued, 0);
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
  scanner.request(images('old:1'));
  await scanner.idle();
  clock.now += hours(1);
  scanner.request(images('newer:1'));
  await scanner.idle();
  trivy.scanned.length = 0;

  clock.now += hours(30);
  scanner.request(images('newer:1', 'old:1', 'brand-new:1'));
  await scanner.idle();

  assert.deepEqual(trivy.scanned, ['brand-new:1', 'old:1', 'newer:1']);
});

test('a scan that fails says why with a short code, and the others go on', async () => {
  const { scanner, trivy } = await harness();
  trivy.answers.set('private:1', new ScanFailure('unauthorized'));
  trivy.answers.set('local:1', new ScanFailure('not-found'));
  trivy.answers.set('scratch:1', { SchemaVersion: 2 });
  trivy.answers.set('ok:1', report(vulnerability({ Severity: 'LOW' })));

  scanner.request(images('private:1', 'local:1', 'scratch:1', 'ok:1'));
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
  scanner.request(images('slow:1', 'gone:1'));
  await scanner.idle();

  clock.now += hours(2);
  assert.equal(scanner.request(images('slow:1', 'gone:1')).queued, 1);
  await scanner.idle();
  assert.deepEqual(trivy.scanned, ['slow:1', 'gone:1', 'slow:1']);

  clock.now += hours(23);
  assert.equal(scanner.request(images('gone:1')).queued, 1);
});

test('an error that is not a scan failure is an error, and the cache is cleaned after every scan', async () => {
  const { scanner, trivy } = await harness();
  trivy.scan = () => Promise.reject(new Error('boom'));

  scanner.request(images('a:1', 'b:1'));
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

  scanner.request(images('a:1'));
  await scanner.idle();

  assert.equal(scanner.results().images[0].state, 'scanned');
});

test('results survive a restart, and an image nobody names for a week is forgotten', async () => {
  const first = await harness();
  first.scanner.request(images('kept:1', 'dropped:1'));
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
  second.scanner.request(images('kept:1'));
  await second.scanner.idle();
  second.clock.now += hours(24 * 2);
  second.scanner.request(images('kept:1'));
  await second.scanner.idle();

  assert.deepEqual(
    second.scanner.results().images.map((image) => image.ref),
    ['kept:1'],
  );
});

test('a results file that cannot be read starts empty', async () => {
  const { dir } = await harness();
  await writeFile(join(dir, 'results.json'), '{ not json');

  const again = await harness(dir);

  assert.deepEqual(again.scanner.results().images, []);
});

test('beyond the maximum of tracked images a new reference is ignored and counted, a known one is still served', async () => {
  const { scanner } = await harness(undefined, 2);

  assert.deepEqual(scanner.request(images('a:1', 'b:1', 'c:1')), { queued: 2, ignored: 1 });
  await scanner.idle();
  assert.deepEqual(scanner.request(images('a:1', 'd:1')), { queued: 0, ignored: 1 });
  assert.deepEqual(
    scanner.results().images.map((image) => image.ref),
    ['a:1', 'b:1'],
  );
});

test('a release is scanned through its source and answered with its tag, scanned or not', async () => {
  const { scanner, trivy, releases: source } = await harness();
  source.answers.set('example/app', { report: report(vulnerability({ Severity: 'CRITICAL' })), version: 'v2.1.0' });

  assert.equal(scanner.request(releases('example/app')).queued, 1);
  assert.deepEqual(
    scanner.results().images.map((r) => [r.kind, r.ref, r.state, r.version]),
    [['release', 'example/app', 'scanning', undefined]],
  );
  await scanner.idle();

  const [result] = scanner.results().images;
  assert.deepEqual(
    [result.kind, result.state, result.version, result.counts.critical],
    ['release', 'scanned', 'v2.1.0', 1],
  );
  assert.deepEqual(source.scanned, ['example/app']);
  assert.deepEqual(trivy.scanned, [], 'the image runner is not used for a release');
  assert.equal(trivy.cleaned, 1);
});

test('an image never carries a version, and a failed release loses the one it had', async () => {
  const { scanner, releases: source, clock } = await harness();
  scanner.request([...images('app:1'), ...releases('example/app')]);
  await scanner.idle();
  assert.deepEqual(
    scanner.results().images.map((r) => [r.kind, 'version' in r]),
    [
      ['image', false],
      ['release', true],
    ],
  );

  source.answers.set('example/app', new ScanFailure('not-found'));
  clock.now += hours(25);
  scanner.request(releases('example/app'));
  await scanner.idle();

  const release = scanner.results().images.find((r) => r.kind === 'release');
  assert.deepEqual(
    [release?.state, release?.reason, release !== undefined && 'version' in release],
    ['failed', 'not-found', false],
  );
});

test('a release and an image with the same text are tracked, queued and forgotten apart', async () => {
  const { scanner, trivy, releases: source } = await harness(undefined, 2);

  assert.deepEqual(scanner.request([...images('a/b'), ...releases('a/b'), ...releases('c/d')]), {
    queued: 2,
    ignored: 1,
  });
  await scanner.idle();

  assert.deepEqual(trivy.scanned, ['a/b']);
  assert.deepEqual(source.scanned, ['a/b']);
  assert.deepEqual(
    scanner.results().images.map((r) => [r.kind, r.ref]),
    [
      ['image', 'a/b'],
      ['release', 'a/b'],
    ],
  );
  assert.deepEqual(scanner.request(releases('a/b')), { queued: 0, ignored: 0 });
});

test('results are sorted by kind, then by reference', async () => {
  const { scanner } = await harness();
  scanner.request([...releases('a/a'), ...images('z:1', 'b:1'), ...releases('0/z')]);
  await scanner.idle();

  assert.deepEqual(
    scanner.results().images.map((r) => `${r.kind} ${r.ref}`),
    ['image b:1', 'image z:1', 'release 0/z', 'release a/a'],
  );
});

test('a release without an SBOM, or with an SBOM that lists nothing, says no-sbom and waits a day, not an hour', async () => {
  const { scanner, releases: source, clock } = await harness();
  source.answers.set('example/none', new ScanFailure('no-sbom'));
  source.answers.set('example/empty', { report: { SchemaVersion: 2 }, version: 'v1' });
  source.answers.set('example/slow', new ScanFailure('timeout'));
  scanner.request(releases('example/none', 'example/empty', 'example/slow'));
  await scanner.idle();
  assert.deepEqual(
    scanner.results().images.map((r) => [r.ref, r.state, r.reason]),
    [
      ['example/empty', 'failed', 'no-sbom'],
      ['example/none', 'failed', 'no-sbom'],
      ['example/slow', 'failed', 'timeout'],
    ],
  );

  clock.now += hours(2);
  assert.equal(
    scanner.request(releases('example/none', 'example/empty', 'example/slow')).queued,
    1,
    'only the timeout',
  );
  await scanner.idle();

  clock.now += hours(23);
  assert.equal(scanner.request(releases('example/none', 'example/empty')).queued, 2);
});

test('a release with no results is no-sbom, an image with none stays unsupported', async () => {
  const { scanner, trivy, releases: source } = await harness();
  trivy.answers.set('scratch:1', { Results: [] });
  source.answers.set('example/empty', { report: { Results: [] }, version: 'v1' });
  scanner.request([...images('scratch:1'), ...releases('example/empty')]);
  await scanner.idle();

  assert.deepEqual(
    scanner.results().images.map((r) => r.reason),
    ['unsupported', 'no-sbom'],
  );
});

test('a file written before releases existed has no kind: its records are images, and release records survive too', async () => {
  const { dir } = await harness();
  const old = {
    state: 'scanned',
    scannedAt: '2026-11-24T10:00:00.000Z',
    attemptedAt: Date.parse('2026-11-25T09:00:00Z'),
  };
  await writeFile(
    join(dir, 'results.json'),
    JSON.stringify({
      database: {},
      records: [
        {
          image: 'old:1',
          requestedAt: Date.parse('2026-11-25T09:00:00Z'),
          ...old,
          counts: { critical: 0, high: 3, medium: 0, low: 0, unknown: 0 },
        },
        { image: 'old/rel', kind: 'release', version: 'v3', requestedAt: Date.parse('2026-11-25T09:00:00Z'), ...old },
        { image: 'junk', kind: 'sbom', requestedAt: 0 },
      ],
    }),
  );

  const { scanner, trivy } = await harness(dir);

  assert.deepEqual(
    scanner.results().images.map((r) => [r.kind, r.ref, r.state, r.version, r.counts.high]),
    [
      ['image', 'old:1', 'scanned', undefined, 3],
      ['release', 'old/rel', 'scanned', 'v3', 0],
    ],
  );
  assert.equal(scanner.request(images('old:1')).queued, 0, 'the old record is the image, not a new one');
  await scanner.idle();
  assert.deepEqual(trivy.scanned, []);
  const saved = JSON.parse(await readFile(join(dir, 'results.json'), 'utf8')) as { records: { kind?: string }[] };
  assert.deepEqual(
    saved.records.map((r) => r.kind),
    ['image', 'release'],
  );
});
