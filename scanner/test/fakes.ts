import { mkdtemp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

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

  clean(): Promise<void> {
    this.cleaned++;
    return Promise.resolve();
  }

  database(): Promise<DatabaseInfo> {
    return Promise.resolve(this.database_);
  }
}

export interface Harness {
  dir: string;
  trivy: FakeTrivy;
  scanner: Scanner;
  clock: { now: number };
}

const hour = 3_600_000;

export async function harness(dir?: string): Promise<Harness> {
  const folder = dir ?? (await mkdtemp(join(tmpdir(), 'portarr-scanner-')));
  const trivy = new FakeTrivy();
  const clock = { now: Date.parse('2026-11-25T10:00:00Z') };
  const scanner = new Scanner(trivy, new ResultStore(folder), {
    rescanAfterMs: 24 * hour,
    forgetAfterMs: 7 * 24 * hour,
    now: () => clock.now,
  });
  await scanner.init();
  return { dir: folder, trivy, scanner, clock };
}

export const hours = (count: number): number => count * hour;
