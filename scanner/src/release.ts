// The SBOM published with the latest release of a GitHub repository: found through the releases API, downloaded into a
// work folder of its own, read by Trivy, and the file removed whatever happens. Public repositories only, read-only.

import { mkdir, mkdtemp, open, rm } from 'node:fs/promises';
import { join } from 'node:path';

import { ScanFailure, type TrivyRunner } from './trivy.ts';

export const maxSbomBytes = 50 * 1024 * 1024;
const maxApiBytes = 8 * 1024 * 1024;
const maxVersionLength = 100;

/** What the scanner needs of a release: the Trivy report of its SBOM and the tag it was published under. */
export interface ReleaseScan {
  report: unknown;
  version: string;
}

export interface ReleaseSource {
  scan(ref: string, runner: TrivyRunner): Promise<ReleaseScan>;
}

export type FetchLike = (url: string, init: RequestInit) => Promise<Response>;

export interface ReleaseSourceOptions {
  /** Where the downloaded SBOM lives while Trivy reads it: on disk, never in /tmp (a RAM tmpfs in the container). */
  workDir: string;
  /** Lifts the anonymous rate limit of the GitHub API; sent to the API only, never logged. */
  token?: string;
  fetch?: FetchLike;
  apiTimeoutMs?: number;
  downloadTimeoutMs?: number;
}

interface Asset {
  name: string;
  size: number;
  url: string;
}

// CycloneDX first, then SPDX, then anything named like an SBOM.
const assetPreference = [/\.cdx\.json$/i, /\.spdx\.json$/i, /sbom.*\.json$/i];

function isGithubDownload(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname === 'github.com' && url.username === '' && url.password === '';
  } catch {
    return false;
  }
}

/** The best SBOM of a release's assets that is small enough and comes from github.com, if any. */
export function chooseAsset(assets: unknown): Asset | undefined {
  if (!Array.isArray(assets)) {
    return undefined;
  }
  const usable: Asset[] = [];
  for (const asset of assets as { name?: unknown; size?: unknown; browser_download_url?: unknown }[]) {
    if (
      typeof asset?.name === 'string' &&
      typeof asset.size === 'number' &&
      asset.size > 0 &&
      asset.size <= maxSbomBytes &&
      typeof asset.browser_download_url === 'string' &&
      isGithubDownload(asset.browser_download_url)
    ) {
      usable.push({ name: asset.name, size: asset.size, url: asset.browser_download_url });
    }
  }
  for (const pattern of assetPreference) {
    const found = usable.find((asset) => pattern.test(asset.name));
    if (found !== undefined) {
      return found;
    }
  }
  return undefined;
}

export function sanitiseVersion(tag: unknown): string | undefined {
  if (typeof tag !== 'string') {
    return undefined;
  }
  const version = tag.replaceAll(/[^A-Za-z0-9._+-]/g, '').slice(0, maxVersionLength);
  return version === '' ? undefined : version;
}

/** Network failures: the abort of our own timer is a timeout, the rest an error. */
function networkFailure(error: unknown): ScanFailure {
  if (error instanceof ScanFailure) {
    return error;
  }
  const name = (error as { name?: unknown } | null)?.name;
  return new ScanFailure(name === 'TimeoutError' || name === 'AbortError' ? 'timeout' : 'error');
}

/** The body, chunk by chunk, stopping with `tooBig` as soon as it passes the cap: a server's size claim is not trusted. */
async function* chunksOf(response: Response, max: number, tooBig: ScanFailure): AsyncGenerator<Uint8Array> {
  const declared = Number(response.headers.get('content-length'));
  if (Number.isFinite(declared) && declared > max) {
    throw tooBig;
  }
  if (response.body === null) {
    return;
  }
  let size = 0;
  for await (const chunk of response.body as unknown as AsyncIterable<Uint8Array>) {
    size += chunk.length;
    if (size > max) {
      throw tooBig;
    }
    yield chunk;
  }
}

export class ReleaseSbomSource implements ReleaseSource {
  private readonly workDir: string;
  private readonly token: string | undefined;
  private readonly fetcher: FetchLike;
  private readonly apiTimeoutMs: number;
  private readonly downloadTimeoutMs: number;

  constructor(options: ReleaseSourceOptions) {
    this.workDir = options.workDir;
    this.token = options.token;
    this.fetcher = options.fetch ?? ((url, init) => fetch(url, init));
    this.apiTimeoutMs = options.apiTimeoutMs ?? 30_000;
    this.downloadTimeoutMs = options.downloadTimeoutMs ?? 120_000;
  }

  async scan(ref: string, runner: TrivyRunner): Promise<ReleaseScan> {
    const { asset, version } = await this.latest(ref);
    await mkdir(this.workDir, { recursive: true, mode: 0o700 });
    const dir = await mkdtemp(join(this.workDir, 'sbom-'));
    try {
      const file = join(dir, 'sbom.json');
      await this.download(asset, file);
      return { report: await runner.scanSbom(file), version };
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  }

  private async latest(ref: string): Promise<{ asset: Asset; version: string }> {
    const headers: Record<string, string> = {
      Accept: 'application/vnd.github+json',
      'User-Agent': 'portarr-scanner',
      'X-GitHub-Api-Version': '2022-11-28',
    };
    if (this.token !== undefined) {
      headers.Authorization = `Bearer ${this.token}`;
    }
    let body: unknown;
    try {
      const response = await this.fetcher(`https://api.github.com/repos/${ref}/releases/latest`, {
        headers,
        signal: AbortSignal.timeout(this.apiTimeoutMs),
      });
      this.checkStatus(response);
      const chunks: Uint8Array[] = [];
      for await (const chunk of chunksOf(response, maxApiBytes, new ScanFailure('error'))) {
        chunks.push(chunk);
      }
      body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    } catch (error) {
      throw error instanceof SyntaxError ? new ScanFailure('error') : networkFailure(error);
    }
    const release = body as { tag_name?: unknown; assets?: unknown } | null;
    const version = sanitiseVersion(release?.tag_name);
    if (version === undefined) {
      throw new ScanFailure('error');
    }
    const asset = chooseAsset(release?.assets);
    if (asset === undefined) {
      throw new ScanFailure('no-sbom');
    }
    return { asset, version };
  }

  private checkStatus(response: Response): void {
    if (response.ok) {
      return;
    }
    if (response.status === 404) {
      // No such repository, or none of its releases is published (the API answers the same).
      throw new ScanFailure('not-found');
    }
    const rateLimited = response.status === 429 || response.headers.get('x-ratelimit-remaining') === '0';
    if (response.status === 401 || (response.status === 403 && !rateLimited)) {
      throw new ScanFailure('unauthorized');
    }
    throw new ScanFailure('error');
  }

  private async download(asset: Asset, file: string): Promise<void> {
    try {
      // The token stays with the API: the download goes to github.com, then to wherever it redirects.
      const response = await this.fetcher(asset.url, {
        headers: { 'User-Agent': 'portarr-scanner' },
        signal: AbortSignal.timeout(this.downloadTimeoutMs),
      });
      if (!response.ok) {
        throw new ScanFailure('error');
      }
      const handle = await open(file, 'w', 0o600);
      try {
        for await (const chunk of chunksOf(response, maxSbomBytes, new ScanFailure('no-sbom'))) {
          await handle.write(chunk);
        }
      } finally {
        await handle.close();
      }
    } catch (error) {
      throw networkFailure(error);
    }
  }
}
