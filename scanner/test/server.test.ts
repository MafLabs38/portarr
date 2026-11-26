import assert from 'node:assert/strict';
import type { AddressInfo } from 'node:net';
import { after, before, test } from 'node:test';

import { createScannerServer, secretMatches } from '../src/server.ts';
import { type Harness, harness } from './fakes.ts';

const secret = 'example-secret-'.repeat(3);
const target = (ref: string) => ({ kind: 'image', ref });
const auth = { Authorization: `Bearer ${secret}`, 'Content-Type': 'application/json' };
let base = '';
let context: Harness;
let close: () => void = () => undefined;

before(async () => {
  context = await harness();
  const server = createScannerServer(context.scanner, secret);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  close = () => server.close();
});

after(() => close());

test('secretMatches compares the bearer token only', () => {
  assert.equal(secretMatches(`Bearer ${secret}`, secret), true);
  assert.equal(secretMatches(`Bearer ${secret}x`, secret), false);
  assert.equal(secretMatches(secret, secret), false);
  assert.equal(secretMatches(undefined, secret), false);
});

test('every route but /healthz needs the secret', async () => {
  assert.equal((await fetch(`${base}/healthz`)).status, 200);
  const missing = await fetch(`${base}/v1/results`);
  assert.equal(missing.status, 401);
  assert.match(missing.headers.get('www-authenticate') ?? '', /Bearer/);
  assert.equal((await fetch(`${base}/v1/results`, { headers: { Authorization: 'Bearer wrong' } })).status, 401);
  assert.equal((await fetch(`${base}/v1/scans`, { method: 'POST', body: '{}' })).status, 401);
  assert.equal((await fetch(`${base}/v1/results`, { headers: auth })).status, 200);
});

test('POST /v1/scans queues the images and says how many, GET /v1/results answers with them', async () => {
  const posted = await fetch(`${base}/v1/scans`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify({ targets: [target('app:1'), target('lscr.io/team/web:2')] }),
  });
  assert.equal(posted.status, 202);
  assert.deepEqual(await posted.json(), { queued: 2, ignored: 0 });
  await context.scanner.idle();

  const results = (await (await fetch(`${base}/v1/results`, { headers: auth })).json()) as {
    generatedAt: string;
    database: { version?: string };
    images: { kind: string; ref: string; state: string; counts: { high: number } }[];
  };
  assert.ok(results.generatedAt);
  assert.equal(results.database.version, '2');
  assert.deepEqual(
    results.images.map((image) => [image.kind, image.ref, image.state, image.counts.high]),
    [
      ['image', 'app:1', 'scanned', 1],
      ['image', 'lscr.io/team/web:2', 'scanned', 1],
    ],
  );

  const again = await fetch(`${base}/v1/scans`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify({ targets: [target('app:1')] }),
  });
  assert.deepEqual(await again.json(), { queued: 0, ignored: 0 });
});

test('a release target is queued and answered with its tag, next to an image of the same text', async () => {
  const posted = await fetch(`${base}/v1/scans`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify({
      targets: [
        { kind: 'release', ref: 'example/app' },
        target('example/app'),
        { kind: 'release', ref: 'example/app' },
      ],
    }),
  });
  assert.deepEqual(await posted.json(), { queued: 2, ignored: 0 });
  await context.scanner.idle();

  const results = (await (await fetch(`${base}/v1/results`, { headers: auth })).json()) as {
    images: { kind: string; ref: string; state: string; version?: string }[];
  };
  assert.deepEqual(
    results.images
      .filter((image) => image.ref === 'example/app')
      .map((image) => [image.kind, image.state, image.version]),
    [
      ['image', 'scanned', undefined],
      ['release', 'scanned', 'v1.0.0'],
    ],
  );
});

test('a request that is not a list of plain references is refused, nothing is queued', async () => {
  for (const body of [
    '{ "targets": [{ "kind": "image", "ref": "--help" }] }',
    '{ "targets": [] }',
    '{ "targets": [{ "kind": "sbom", "ref": "release.json" }] }',
    '{ "targets": [{ "kind": "release", "ref": "-owner/name" }] }',
    '{ "targets": [{ "kind": "release", "ref": "app:1" }] }',
    '{ "images": ["app:1"] }',
    '{}',
    'null',
    '[1',
    '{ "targets": [{ "kind": "image", "ref": "a b" }] }',
  ]) {
    const response = await fetch(`${base}/v1/scans`, { method: 'POST', headers: auth, body });
    assert.equal(response.status, 400, body);
  }
  const results = (await (await fetch(`${base}/v1/results`, { headers: auth })).json()) as { images: unknown[] };
  assert.equal(results.images.length, 4);
});

test('a body that is too large is refused, and so are the wrong method and an unknown route', async () => {
  const huge = await fetch(`${base}/v1/scans`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify({ targets: [target('a'.repeat(200_000))] }),
  });
  assert.equal(huge.status, 413);
  assert.equal((await fetch(`${base}/v1/results`, { method: 'POST', headers: auth, body: '{}' })).status, 405);
  assert.equal((await fetch(`${base}/v1/scans`, { headers: auth })).status, 405);
  assert.equal((await fetch(`${base}/v1/other`, { headers: auth })).status, 404);
});
