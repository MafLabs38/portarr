import assert from 'node:assert/strict';
import test from 'node:test';

import {
  isImageReference,
  isReleaseReference,
  maxImages,
  maxReferenceLength,
  parseTargets,
  targetKey,
} from '../src/reference.ts';

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

test('a kind other than image or release is refused with a clear error naming both', () => {
  for (const entry of [{ kind: 'sbom', ref: 'a:1' }, { ref: 'a:1' }, 'a:1', null]) {
    const parsed = parseTargets([entry]);
    assert.ok('error' in parsed);
    assert.match(parsed.error, /kind must be one of: image, release/);
  }
  const empty = parseTargets([]);
  assert.ok('error' in empty);
  assert.match(empty.error, /image.*release/);
});

test('a release reference is a GitHub owner/name', () => {
  for (const reference of ['owner/name', 'Some-Org/my.repo_1', 'a/b', `${'a'.repeat(100)}/${'b'.repeat(100)}`]) {
    assert.equal(isReleaseReference(reference), true, reference);
  }
  for (const reference of [
    '',
    'owner',
    'owner/',
    '/name',
    'owner/name/extra',
    '-owner/name',
    '.owner/name',
    'owner/..',
    'owner/.',
    '../name',
    'owner/na me',
    'owner/name?x=1',
    'owner/name#frag',
    'owner/name\n',
    'owner:1/name',
    `${'a'.repeat(101)}/b`,
    `a/${'b'.repeat(101)}`,
  ]) {
    assert.equal(isReleaseReference(reference), false, JSON.stringify(reference));
  }
  assert.equal(isReleaseReference(42), false);
  assert.equal(isReleaseReference(undefined), false);
});

test('an image and a release with the same text are two targets, each deduplicated on its own', () => {
  const parsed = parseTargets([
    { kind: 'image', ref: 'a/b' },
    { kind: 'release', ref: 'a/b' },
    { kind: 'release', ref: 'a/b' },
    { kind: 'image', ref: 'a/b' },
  ]);
  assert.deepEqual(parsed, {
    targets: [
      { kind: 'image', ref: 'a/b' },
      { kind: 'release', ref: 'a/b' },
    ],
  });
  assert.notEqual(targetKey('image', 'a/b'), targetKey('release', 'a/b'));
});

test('an invalid release reference is refused with a message about releases', () => {
  const parsed = parseTargets([{ kind: 'release', ref: 'not a repo' }]);
  assert.ok('error' in parsed);
  assert.match(parsed.error, /release target's ref must be a GitHub repository/);
});
