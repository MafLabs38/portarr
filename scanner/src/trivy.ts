// The Trivy command line, behind a small interface so that the scanner is tested without it. The reference is one
// element of an argument array: no shell ever sees it.

import { spawn } from 'node:child_process';

export type Reason = 'not-found' | 'unauthorized' | 'timeout' | 'unsupported' | 'error';

/** A scan that did not produce a report, with the short code Portarr shows. */
export class ScanFailure extends Error {
  readonly reason: Reason;
  constructor(reason: Reason) {
    super(reason);
    this.reason = reason;
  }
}

export interface DatabaseInfo {
  version?: string;
  updatedAt?: string;
}

export interface TrivyRunner {
  /** Trivy's JSON report of the image, or a `ScanFailure`. */
  scan(image: string): Promise<unknown>;
  /** Drops what the scan left in the cache (layers, results); the vulnerability database stays. */
  clean(): Promise<void>;
  /** The vulnerability database Trivy holds; empty when it cannot tell. */
  database(): Promise<DatabaseInfo>;
}

const maxOutputBytes = 64 * 1024 * 1024;

/** What Trivy's error text says about why an image could not be read. Authorization first: some registries answer "denied" for a missing image too. */
export function reasonOf(stderr: string): Reason {
  if (/unauthorized|denied|authentication required|\b40[13]\b/i.test(stderr)) {
    return 'unauthorized';
  }
  if (/manifest_unknown|name_unknown|not found|no such host|unable to find the specified image|\b404\b/i.test(stderr)) {
    return 'not-found';
  }
  if (/timeout|deadline exceeded|timed out/i.test(stderr)) {
    return 'timeout';
  }
  return 'error';
}

interface Outcome {
  code: number | null;
  stdout: string;
  stderr: string;
  timedOut: boolean;
  overflowed: boolean;
}

function run(command: string, args: string[], timeoutMs: number): Promise<Outcome> {
  return new Promise((resolve) => {
    const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    const out: Buffer[] = [];
    const err: Buffer[] = [];
    let size = 0;
    let timedOut = false;
    let overflowed = false;
    const timer = setTimeout(() => {
      timedOut = true;
      child.kill('SIGKILL');
      // Not waited for: a child of the process may hold the pipes open.
      finish(null);
    }, timeoutMs);
    child.stdout.on('data', (chunk: Buffer) => {
      size += chunk.length;
      if (size > maxOutputBytes) {
        overflowed = true;
        child.kill('SIGKILL');
        return;
      }
      out.push(chunk);
    });
    // Only the end of the error text is kept: it says why Trivy stopped.
    child.stderr.on('data', (chunk: Buffer) => {
      err.push(chunk);
      if (err.length > 50) {
        err.shift();
      }
    });
    let finished = false;
    const finish = (code: number | null, extra = '') => {
      if (finished) {
        return;
      }
      finished = true;
      clearTimeout(timer);
      resolve({
        code,
        stdout: Buffer.concat(out).toString('utf8'),
        stderr: Buffer.concat(err).toString('utf8') + extra,
        timedOut,
        overflowed,
      });
    };
    child.on('error', (error) => finish(null, error.message));
    child.on('close', (code) => finish(code));
  });
}

export class TrivyCli implements TrivyRunner {
  private readonly command: string;
  private readonly cacheDir: string;
  private readonly timeoutMs: number;
  private readonly nice: boolean;
  private readonly graceMs: number;

  constructor(command: string, cacheDir: string, timeoutMs: number, nice = true, graceMs = 15_000) {
    this.graceMs = graceMs;
    this.command = command;
    this.cacheDir = cacheDir;
    this.timeoutMs = timeoutMs;
    this.nice = nice;
  }

  async scan(image: string): Promise<unknown> {
    const outcome = await this.trivy(
      [
        'image',
        '--image-src',
        'remote',
        '--format',
        'json',
        '--quiet',
        '--no-progress',
        '--scanners',
        'vuln',
        '--timeout',
        `${Math.ceil(this.timeoutMs / 1000)}s`,
        '--cache-dir',
        this.cacheDir,
        '--',
        image,
      ],
      // Trivy stops itself at its own timeout; this one only catches a process that does not.
      this.timeoutMs + this.graceMs,
    );
    if (outcome.timedOut) {
      throw new ScanFailure('timeout');
    }
    if (outcome.overflowed || outcome.code !== 0) {
      throw new ScanFailure(outcome.overflowed ? 'error' : reasonOf(outcome.stderr));
    }
    try {
      return JSON.parse(outcome.stdout) as unknown;
    } catch {
      throw new ScanFailure('error');
    }
  }

  async clean(): Promise<void> {
    await this.trivy(['clean', '--scan-cache', '--cache-dir', this.cacheDir], 60_000);
  }

  async database(): Promise<DatabaseInfo> {
    const outcome = await this.trivy(['version', '--format', 'json', '--cache-dir', this.cacheDir], 30_000);
    try {
      const database = (JSON.parse(outcome.stdout) as { VulnerabilityDB?: { Version?: unknown; UpdatedAt?: unknown } })
        .VulnerabilityDB;
      const version = database?.Version === undefined ? undefined : String(database.Version).slice(0, 50);
      const updatedAt =
        typeof database?.UpdatedAt === 'string' && !Number.isNaN(Date.parse(database.UpdatedAt))
          ? new Date(database.UpdatedAt).toISOString()
          : undefined;
      return { ...(version === undefined ? {} : { version }), ...(updatedAt === undefined ? {} : { updatedAt }) };
    } catch {
      return {};
    }
  }

  private trivy(args: string[], timeoutMs: number): Promise<Outcome> {
    // Low priority: a scan must not slow the machine down.
    return this.nice ? run('nice', ['-n', '15', this.command, ...args], timeoutMs) : run(this.command, args, timeoutMs);
  }
}
