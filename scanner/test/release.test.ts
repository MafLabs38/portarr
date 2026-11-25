import assert from 'node:assert/strict';
import { access, mkdtemp, readdir, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { chooseAsset, type FetchLike, maxSbomBytes, ReleaseSbomSource, sanitiseVersion } from '../src/release.ts';
import { ScanFailure } from '../src/trivy.ts';
import { FakeTrivy, report, vulnerability } from './fakes.ts';

const download = (name: string, size = 100, url = `https://github.com/example/app/releases/download/v1/${name}`) => ({
  name,
  size,
  browser_download_url: url,
});
const json = (body: unknown, init: ResponseInit = {}) => new Response(JSON.stringify(body), init);

interface Call {
  url: string;
  headers: Record<string, string>;
}

/** A GitHub that answers from a table of URL to response (or a function that throws); every call is recorded. */
function fakeFetch(answers: Record<string, () => Response | Promise<Response>>): { fetch: FetchLike; calls: Call[] } {
  const calls: Call[] = [];
  const fetcher: FetchLike = (url, init) => {
    calls.push({ url, headers: { ...(init.headers as Record<string, string>) } });
    const answer = answers[url];
    return answer === undefined ? Promise.resolve(new Response('', { status: 599 })) : Promise.resolve(answer());
  };
  return { fetch: fetcher, calls };
}

const api = 'https://api.github.com/repos/example/app/releases/latest';

async function source(
  answers: Record<string, () => Response | Promise<Response>>,
  token?: string,
): Promise<{ source: ReleaseSbomSource; workDir: string; calls: Call[] }> {
  const workDir = join(await mkdtemp(join(tmpdir(), 'portarr-release-')), 'work');
  const { fetch, calls } = fakeFetch(answers);
  return {
    source: new ReleaseSbomSource({ workDir, fetch, ...(token === undefined ? {} : { token }) }),
    workDir,
    calls,
  };
}

/** A Trivy that reads the SBOM file it is given, so that the test sees what was downloaded. */
class ReadingTrivy extends FakeTrivy {
  files: string[] = [];
  contents: string[] = [];
  override async scanSbom(file: string): Promise<unknown> {
    this.files.push(file);
    this.contents.push(await readFile(file, 'utf8'));
    return report(vulnerability());
  }
}

const sbomUrl = 'https://github.com/example/app/releases/download/v1/app.cdx.json';

test('the SBOM asset is chosen CycloneDX first, then SPDX, then any sbom json, and the others are ignored', () => {
  const names = (assets: unknown) => chooseAsset(assets)?.name;

  assert.equal(names([download('app.spdx.json'), download('sbom.json'), download('app.cdx.json')]), 'app.cdx.json');
  assert.equal(names([download('sbom.json'), download('APP.SPDX.JSON')]), 'APP.SPDX.JSON');
  assert.equal(names([download('app.tar.gz'), download('my-sbom-1.0.json')]), 'my-sbom-1.0.json');
  assert.equal(names([download('app.tar.gz'), download('checksums.txt'), download('sbom.xml')]), undefined);
  assert.equal(names([]), undefined);
  assert.equal(names('nope'), undefined);
  assert.equal(names([null, 3, { name: 4 }, { name: 'a.cdx.json' }]), undefined);
});

test('an asset over 50 MiB, an empty one, or one that does not come from github.com is not a candidate', () => {
  assert.equal(chooseAsset([download('a.cdx.json', maxSbomBytes + 1)]), undefined);
  assert.equal(chooseAsset([download('a.cdx.json', 0)]), undefined);
  assert.equal(chooseAsset([download('a.cdx.json', maxSbomBytes)])?.name, 'a.cdx.json');
  for (const url of [
    'http://github.com/x/a.cdx.json',
    'https://evil.example/github.com/a.cdx.json',
    'https://github.com.evil.example/a.cdx.json',
    'https://user:pw@github.com/a.cdx.json',
    'https://objects.githubusercontent.com/a.cdx.json',
    'not a url',
  ]) {
    assert.equal(chooseAsset([download('a.cdx.json', 10, url)]), undefined, url);
  }
  // A big CycloneDX does not hide a usable SPDX.
  assert.equal(chooseAsset([download('a.cdx.json', maxSbomBytes + 1), download('a.spdx.json')])?.name, 'a.spdx.json');
});

test('the tag is cleaned to 100 characters of a safe alphabet', () => {
  assert.equal(sanitiseVersion('v1.2.3-rc.1+build5'), 'v1.2.3-rc.1+build5');
  assert.equal(sanitiseVersion('v1 <script>\n/..'), 'v1script..');
  assert.equal(sanitiseVersion('v'.repeat(300))?.length, 100);
  assert.equal(sanitiseVersion('<>'), undefined);
  assert.equal(sanitiseVersion(7), undefined);
});

test('the latest release is read, its SBOM downloaded, read by Trivy, and the folder removed', async () => {
  const {
    source: releases,
    workDir,
    calls,
  } = await source({
    [api]: () => json({ tag_name: 'v1.0.0', assets: [download('app.cdx.json')] }),
    [sbomUrl]: () => new Response('{"bomFormat":"CycloneDX"}'),
  });
  const trivy = new ReadingTrivy();

  const result = await releases.scan('example/app', trivy);

  assert.equal(result.version, 'v1.0.0');
  assert.deepEqual(result.report, report(vulnerability()));
  assert.deepEqual(trivy.contents, ['{"bomFormat":"CycloneDX"}']);
  assert.ok(trivy.files[0].startsWith(`${workDir}/sbom-`), 'in the work folder, not in /tmp');
  await assert.rejects(access(trivy.files[0]));
  assert.deepEqual(await readdir(workDir), []);
  assert.deepEqual(
    calls.map((call) => call.url),
    [api, sbomUrl],
  );
  assert.deepEqual(calls[0].headers, {
    Accept: 'application/vnd.github+json',
    'User-Agent': 'portarr-scanner',
    'X-GitHub-Api-Version': '2022-11-28',
  });
});

test('the token goes to the API only, and only when configured', async () => {
  const answers = {
    [api]: () => json({ tag_name: 'v1', assets: [download('app.cdx.json')] }),
    [sbomUrl]: () => new Response('{}'),
  };
  const withToken = await source(answers, 'ghp_example');
  await withToken.source.scan('example/app', new ReadingTrivy());
  assert.equal(withToken.calls[0].headers.Authorization, 'Bearer ghp_example');
  assert.equal(withToken.calls[1].headers.Authorization, undefined);

  const without = await source(answers);
  await without.source.scan('example/app', new ReadingTrivy());
  assert.equal('Authorization' in without.calls[0].headers, false);
});

test('the answers of the API become short codes', async () => {
  const codes: [number, Record<string, string>, string][] = [
    [404, {}, 'not-found'],
    [401, {}, 'unauthorized'],
    [403, {}, 'unauthorized'],
    [403, { 'x-ratelimit-remaining': '0' }, 'error'],
    [403, { 'x-ratelimit-remaining': '12' }, 'unauthorized'],
    [429, {}, 'error'],
    [500, {}, 'error'],
  ];
  for (const [status, headers, reason] of codes) {
    const { source: releases } = await source({ [api]: () => new Response('{}', { status, headers }) });
    await assert.rejects(
      releases.scan('example/app', new FakeTrivy()),
      { reason },
      `${status} ${JSON.stringify(headers)}`,
    );
  }
});

test('a network error, a timeout, a body that is not JSON or has no tag are failures, not crashes', async () => {
  const cases: [() => Response | Promise<Response>, string][] = [
    [() => Promise.reject(new TypeError('fetch failed')), 'error'],
    [() => Promise.reject(new DOMException('timed out', 'TimeoutError')), 'timeout'],
    [() => Promise.reject(new DOMException('aborted', 'AbortError')), 'timeout'],
    [() => new Response('<html>'), 'error'],
    [() => json({ assets: [download('a.cdx.json')] }), 'error'],
    [() => json({ tag_name: '<>', assets: [] }), 'error'],
    [() => json(null), 'error'],
    [() => new Response(null), 'error'],
  ];
  for (const [answer, reason] of cases) {
    const { source: releases } = await source({ [api]: answer });
    await assert.rejects(releases.scan('example/app', new FakeTrivy()), { reason });
  }
});

test('a release with no SBOM asset says no-sbom and downloads nothing', async () => {
  for (const assets of [[], [download('app.tar.gz')], undefined, [download('a.cdx.json', maxSbomBytes + 1)]]) {
    const { source: releases, calls } = await source({ [api]: () => json({ tag_name: 'v1', assets }) });
    await assert.rejects(releases.scan('example/app', new FakeTrivy()), { reason: 'no-sbom' });
    assert.equal(calls.length, 1);
  }
});

test('a download that fails, or goes past 50 MiB while it streams, is refused and leaves no file', async () => {
  const release = () => json({ tag_name: 'v1', assets: [download('app.cdx.json', 10)] });
  const big = new Uint8Array(maxSbomBytes / 2 + 1);
  const streamed = () =>
    new Response(
      new ReadableStream({
        start(controller) {
          controller.enqueue(big);
          controller.enqueue(big);
          controller.close();
        },
      }),
    );
  const cases: [() => Response | Promise<Response>, string][] = [
    [() => new Response('', { status: 404 }), 'error'],
    [() => Promise.reject(new TypeError('fetch failed')), 'error'],
    [() => Promise.reject(new DOMException('timed out', 'TimeoutError')), 'timeout'],
    [streamed, 'no-sbom'],
    [() => new Response('{}', { headers: { 'content-length': String(maxSbomBytes + 1) } }), 'no-sbom'],
  ];
  for (const [answer, reason] of cases) {
    const { source: releases, workDir } = await source({ [api]: release, [sbomUrl]: answer });
    const trivy = new ReadingTrivy();
    await assert.rejects(releases.scan('example/app', trivy), { reason });
    assert.deepEqual(trivy.files, []);
    assert.deepEqual(await readdir(workDir), []);
  }
});

test('the work folder is cleaned when Trivy fails, whatever the failure', async () => {
  const { source: releases, workDir } = await source({
    [api]: () => json({ tag_name: 'v1', assets: [download('app.cdx.json')] }),
    [sbomUrl]: () => new Response('{}'),
  });
  const trivy = new FakeTrivy();
  trivy.scanSbom = () => Promise.reject(new ScanFailure('no-sbom'));
  await assert.rejects(releases.scan('example/app', trivy), { reason: 'no-sbom' });
  assert.deepEqual(await readdir(workDir), []);

  trivy.scanSbom = () => Promise.reject(new Error('boom'));
  await assert.rejects(releases.scan('example/app', trivy), /boom/);
  assert.deepEqual(await readdir(workDir), []);
});

test('an asset from another host is never downloaded', async () => {
  const evil = 'https://evil.example/app.cdx.json';
  const { source: releases, calls } = await source({
    [api]: () => json({ tag_name: 'v1', assets: [download('app.cdx.json', 10, evil)] }),
    [evil]: () => new Response('{}'),
  });

  await assert.rejects(releases.scan('example/app', new FakeTrivy()), { reason: 'no-sbom' });
  assert.deepEqual(
    calls.map((call) => call.url),
    [api],
  );
});

test('without an injected fetch the global one is used, and a refused connection is an error', async () => {
  const workDir = join(await mkdtemp(join(tmpdir(), 'portarr-release-')), 'work');
  const original = globalThis.fetch;
  globalThis.fetch = () => Promise.reject(new TypeError('fetch failed'));
  try {
    await assert.rejects(new ReleaseSbomSource({ workDir }).scan('example/app', new FakeTrivy()), { reason: 'error' });
  } finally {
    globalThis.fetch = original;
  }
});
