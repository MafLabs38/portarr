import assert from 'node:assert/strict';
import test from 'node:test';

import { isImageReference, maxImages, maxReferenceLength, parseTargets } from '../src/reference.ts';

test('a plain image reference is accepted', () => {
  for (const reference of [
    'postgres:16',
    'lscr.io/linuxserver/sonarr:4.0.1',
    'registry.example:5000/team/app:1.2',
    'ghcr.io/example/app@sha256:' + 'a'.repeat(64),
    'ghcr.io/example/app:1.2@sha256:' + 'b'.repeat(64),
  ]) {
    assert.equal(isImageReference(reference), true, reference);
  }
});

test('anything the CLI could take for an option or a shell could interpret is refused', () => {
  for (const reference of [
    '--output=/etc/passwd',
    '-v',
    'app; rm -rf /',
    'app && id',
    'app name',
    'app\nid',
    '$(id)',
    '`id`',
    'app|cat',
    "app'x",
    '',
    '.hidden',
    '/absolute',
    'a'.repeat(maxReferenceLength + 1),
  ]) {
    assert.equal(isImageReference(reference), false, JSON.stringify(reference));
  }
  assert.equal(isImageReference(42), false);
  assert.equal(isImageReference(undefined), false);
});

const image = (ref: unknown) => ({ kind: 'image', ref });

test('a request lists targets of kind image, deduplicated, at most 200', () => {
  assert.deepEqual(parseTargets([image('a:1'), image('b:1'), image('a:1')]), {
    targets: [
      { kind: 'image', ref: 'a:1' },
      { kind: 'image', ref: 'b:1' },
    ],
  });
  assert.ok('error' in parseTargets([]));
  assert.ok('error' in parseTargets('a:1'));
  assert.ok('error' in parseTargets([image('a:1'), image('-x')]));
  assert.ok('error' in parseTargets([image(42)]));
  assert.ok('error' in parseTargets(Array.from({ length: maxImages + 1 }, (_, index) => image(`app:${index}`))));
  assert.ok('targets' in parseTargets(Array.from({ length: maxImages }, (_, index) => image(`app:${index}`))));
});

test('a kind other than image is refused with a clear error', () => {
  for (const entry of [{ kind: 'sbom', ref: 'a:1' }, { ref: 'a:1' }, 'a:1', null]) {
    const parsed = parseTargets([entry]);
    assert.ok('error' in parsed);
    assert.match(parsed.error, /kind must be one of: image/);
  }
});
