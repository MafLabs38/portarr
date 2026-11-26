import { mkdtemp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import type { Target } from '../src/reference.ts';
import type { ReleaseScan, ReleaseSource } from '../src/release.ts';
import { Scanner } from '../src/scanner.ts';
import { ResultStore } from '../src/store.ts';
import { type DatabaseInfo, ScanFailure, type TrivyRunner } from '../src/trivy.ts';

export function vulnerability(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    VulnerabilityID: 'CVE-2026-0001',
    PkgName: 'openssl',
    InstalledVersion: '3.0.1',
    FixedVersion: '3.0.2',
    Severity: 'HIGH',
    Title: 'A flaw in openssl',
    PrimaryURL: 'https://avd.example/cve-2026-0001',
    ...overrides,
  };
}

export function report(...vulnerabilities: Record<string, unknown>[]): unknown {
  return {
    SchemaVersion: 2,
    Results: [{ Target: 'image (debian 12)', Class: 'os-pkgs', Vulnerabilities: vulnerabilities }],
  };
}

export const images = (...refs: string[]): Target[] => refs.map((ref) => ({ kind: 'image', ref }));
export const releases = (...refs: string[]): Target[] => refs.map((ref) => ({ kind: 'release', ref }));

/** A Trivy that answers from a table: a report, or the failure to throw. */
export class FakeTrivy implements TrivyRunner {
  readonly scanned: string[] = [];
  cleaned = 0;
  answers = new Map<string, unknown>();
  database_: DatabaseInfo = { version: '2', updatedAt: '2026-11-25T00:00:00.000Z' };
  gate: Promise<void> | undefined;

  async scan(image: string): Promise<unknown> {
    this.scanned.push(image);
    await this.gate;
    const answer = this.answers.get(image) ?? report(vulnerability());
    if (answer instanceof ScanFailure) {
      throw answer;
    }
    return answer;
  }

  scanSbom(file: string): Promise<unknown> {
    return this.scan(file);
  }

  clean(): Promise<void> {
    this.cleaned++;
    return Promise.resolve();
  }

  database(): Promise<DatabaseInfo> {
    return Promise.resolve(this.database_);
  }
}

/** A source of releases that answers from a table: a scan (a report and a tag), or the failure to throw. */
export class FakeReleases implements ReleaseSource {
  readonly scanned: string[] = [];
  answers = new Map<string, ReleaseScan | ScanFailure>();

  scan(ref: string): Promise<ReleaseScan> {
    this.scanned.push(ref);
    const answer = this.answers.get(ref) ?? { report: report(vulnerability()), version: 'v1.0.0' };
    return answer instanceof ScanFailure ? Promise.reject(answer) : Promise.resolve(answer);
  }
}

export interface Harness {
  dir: string;
  trivy: FakeTrivy;
  releases: FakeReleases;
  scanner: Scanner;
  clock: { now: number };
}

const hour = 3_600_000;

export async function harness(dir?: string, maxImages?: number): Promise<Harness> {
  const folder = dir ?? (await mkdtemp(join(tmpdir(), 'portarr-scanner-')));
  const trivy = new FakeTrivy();
  const releaseSource = new FakeReleases();
  const clock = { now: Date.parse('2026-11-25T10:00:00Z') };
  const scanner = new Scanner(trivy, new ResultStore(folder), releaseSource, {
    rescanAfterMs: 24 * hour,
    forgetAfterMs: 7 * 24 * hour,
    ...(maxImages === undefined ? {} : { maxImages }),
    now: () => clock.now,
  });
  await scanner.init();
  return { dir: folder, trivy, releases: releaseSource, scanner, clock };
}

export const hours = (count: number): number => count * hour;
