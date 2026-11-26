// The queue and the worker: images Portarr names are scanned one at a time, the oldest request first, never twice in
// a day. The results live in memory and in one file; the Trivy CLI is the only thing that touches the network.

import { defaultMaxImages } from './config.ts';
import { type Target, targetKey, type TargetKind } from './reference.ts';
import type { ReleaseSource } from './release.ts';
import { type Counts, emptyCounts, type Finding, hasResults, idPattern, maxIds, summarise } from './report.ts';
import type { ResultStore, StoredImage } from './store.ts';
import { type DatabaseInfo, type Reason, ScanFailure, type TrivyRunner } from './trivy.ts';

export type ImageState = 'scanned' | 'queued' | 'scanning' | 'failed';

export interface ImageResult {
  /** What was scanned: a container image, or the SBOM of the latest release of a repository. The pair kind and ref names a result. */
  kind: TargetKind;
  ref: string;
  state: ImageState;
  reason?: Reason;
  scannedAt?: string;
  /** The tag of the release that was scanned; only for a release, only once scanned. */
  version?: string;
  counts: Counts;
  fixable: Counts;
  findings: Finding[];
  /** The distinct ids of every finding, any severity, sorted; only for a scanned target whose result has them. */
  ids?: string[];
  /** Only with `ids`: true when the target had more ids than the companion sends. */
  idsTruncated?: boolean;
}

export interface ScanResults {
  generatedAt: string;
  database: DatabaseInfo;
  images: ImageResult[];
}

export interface ScannerOptions {
  rescanAfterMs: number;
  forgetAfterMs: number;
  /** At most this many targets (images and releases) are tracked; a new one beyond it is ignored (the answer says how many). */
  maxImages?: number;
  /** A scan that failed for no lasting reason (a timeout, an error) is tried again after this. */
  retryAfterMs?: number;
  now?: () => number;
  log?: (message: string) => void;
}

export interface RequestOutcome {
  /** Targets that will be scanned now. */
  queued: number;
  /** New targets refused because the scanner already tracks its maximum. */
  ignored: number;
}

const lastingFailures: ReadonlySet<Reason> = new Set<Reason>(['not-found', 'unauthorized', 'unsupported']);

/** The ids go with a scanned result only, and only a list of strings is passed on (a file is read back as it was). */
function idsOf(record: StoredImage): Pick<ImageResult, 'ids' | 'idsTruncated'> {
  if (record.state !== 'scanned' || !Array.isArray(record.ids)) {
    return {};
  }
  return {
    ids: record.ids.filter((id): id is string => typeof id === 'string' && idPattern.test(id)).slice(0, maxIds),
    idsTruncated: record.idsTruncated === true,
  };
}

/** A record whose kind is settled: an old file knows only images. */
type Tracked = StoredImage & { kind: TargetKind };

export class Scanner {
  private readonly runner: TrivyRunner;
  private readonly store: ResultStore;
  private readonly releases: ReleaseSource;
  private readonly options: Required<ScannerOptions>;
  private records = new Map<string, Tracked>();
  private database: DatabaseInfo = {};
  /** Keys (kind and ref), oldest request first. */
  private readonly queue: string[] = [];
  private current: string | undefined;
  private working: Promise<void> | undefined;
  private saving: Promise<void> = Promise.resolve();

  constructor(runner: TrivyRunner, store: ResultStore, releases: ReleaseSource, options: ScannerOptions) {
    this.runner = runner;
    this.store = store;
    this.releases = releases;
    this.options = {
      retryAfterMs: 3_600_000,
      maxImages: defaultMaxImages,
      now: () => Date.now(),
      log: () => undefined,
      ...options,
    };
  }

  async init(): Promise<void> {
    const stored = await this.store.load();
    this.database = stored.database;
    this.records = new Map(
      stored.records.map((record) => {
        const kind = record.kind ?? 'image';
        return [targetKey(kind, record.image), { ...record, kind }];
      }),
    );
  }

  /**
   * Queues the targets that are new or due. Targets Portarr names stay known for the forget delay; once the scanner
   * tracks its maximum, one it does not know yet is ignored (and counted).
   */
  request(targets: readonly Target[]): RequestOutcome {
    const now = this.options.now();
    this.forget(now);
    const due: { key: string; since: number }[] = [];
    let ignored = 0;
    const unique = new Map(targets.map((target) => [targetKey(target.kind, target.ref), target]));
    for (const [key, target] of unique) {
      const known = this.records.get(key);
      if (known === undefined && this.records.size >= this.options.maxImages) {
        ignored++;
        continue;
      }
      const record: Tracked = known ?? { image: target.ref, kind: target.kind, requestedAt: now };
      record.requestedAt = now;
      this.records.set(key, record);
      if (key !== this.current && !this.queue.includes(key) && this.isDue(record, now)) {
        due.push({ key, since: record.attemptedAt ?? 0 });
      }
    }
    // Targets never scanned go first, then the ones scanned longest ago.
    due.sort((a, b) => a.since - b.since);
    this.queue.push(...due.map((entry) => entry.key));
    this.start();
    if (due.length === 0) {
      void this.persist();
    }
    return { queued: due.length, ignored };
  }

  results(): ScanResults {
    const now = this.options.now();
    const images = [...this.records.entries()].map(([key, record]) => this.resultOf(key, record));
    return {
      generatedAt: new Date(now).toISOString(),
      database: this.database,
      images: images.toSorted((a, b) => a.kind.localeCompare(b.kind) || a.ref.localeCompare(b.ref)),
    };
  }

  /** Resolves when the queue is empty; the tests wait on it. */
  async idle(): Promise<void> {
    while (this.working) {
      await this.working;
    }
    await this.saving;
  }

  private isDue(record: Tracked, now: number): boolean {
    if (record.attemptedAt === undefined) {
      return true;
    }
    // A failure that may pass is tried again soon; a lasting one, and a release without an SBOM (it may publish one with
    // its next version, not within the hour), wait for the normal rescan.
    const reason = record.reason as Reason;
    const retrySoon = record.state === 'failed' && !lastingFailures.has(reason) && reason !== 'no-sbom';
    const wait = retrySoon ? this.options.retryAfterMs : this.options.rescanAfterMs;
    return now - record.attemptedAt >= wait;
  }

  private resultOf(key: string, record: Tracked): ImageResult {
    // An image with a result keeps showing it while it waits for a rescan; the others say where they are in the queue.
    return {
      kind: record.kind,
      ref: record.image,
      state: record.state ?? (key === this.current ? 'scanning' : 'queued'),
      ...(record.reason === undefined ? {} : { reason: record.reason }),
      ...(record.scannedAt === undefined ? {} : { scannedAt: record.scannedAt }),
      ...(record.version === undefined ? {} : { version: record.version }),
      counts: record.counts ?? emptyCounts(),
      fixable: record.fixable ?? emptyCounts(),
      findings: record.findings ?? [],
      ...idsOf(record),
    };
  }

  private forget(now: number): void {
    for (const [key, record] of this.records) {
      if (now - record.requestedAt > this.options.forgetAfterMs && key !== this.current && !this.queue.includes(key)) {
        this.records.delete(key);
      }
    }
  }

  private start(): void {
    if (this.working || this.queue.length === 0) {
      return;
    }
    this.working = this.work().finally(() => {
      this.working = undefined;
      // Targets queued while the worker was finishing are not left waiting.
      this.start();
    });
  }

  private async work(): Promise<void> {
    for (let key = this.queue.shift(); key !== undefined; key = this.queue.shift()) {
      this.current = key;
      await this.scanOne(key);
      this.current = undefined;
    }
  }

  private async scanOne(key: string): Promise<void> {
    const record = this.records.get(key);
    if (record === undefined) {
      return;
    }
    try {
      const { report, version } = await this.report(record);
      if (!hasResults(report)) {
        // Nothing Trivy can read: an image from scratch, or an operating system it does not know; for a release, an
        // SBOM that lists nothing.
        throw new ScanFailure(record.kind === 'release' ? 'no-sbom' : 'unsupported');
      }
      Object.assign(record, summarise(report), {
        state: 'scanned',
        reason: undefined,
        scannedAt: new Date(this.options.now()).toISOString(),
        version,
      });
    } catch (error) {
      const reason: Reason = error instanceof ScanFailure ? error.reason : 'error';
      this.options.log(`scan of ${key} failed: ${reason}`);
      Object.assign(record, {
        state: 'failed',
        reason,
        scannedAt: undefined,
        version: undefined,
        counts: undefined,
        fixable: undefined,
        findings: undefined,
        ids: undefined,
        idsTruncated: undefined,
      });
    }
    record.attemptedAt = this.options.now();
    await this.cleanUp();
    await this.persist();
  }

  private async report(record: Tracked): Promise<{ report: unknown; version?: string }> {
    return record.kind === 'release'
      ? this.releases.scan(record.image, this.runner)
      : { report: await this.runner.scan(record.image) };
  }

  private async cleanUp(): Promise<void> {
    try {
      await this.runner.clean();
      this.database = { ...this.database, ...(await this.runner.database()) };
    } catch (error) {
      this.options.log(`clean-up failed: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  /** One write at a time: the file is replaced, never edited. */
  private persist(): Promise<void> {
    const stored = {
      database: this.database,
      records: [...this.records.values()]
        .filter((record) => record.attemptedAt !== undefined)
        .map((record) => ({ ...record })),
    };
    this.saving = this.saving.then(() =>
      this.store.save(stored).catch((error: unknown) => {
        this.options.log(`results could not be written: ${error instanceof Error ? error.message : String(error)}`);
      }),
    );
    return this.saving;
  }
}
