// The queue and the worker: images Portarr names are scanned one at a time, the oldest request first, never twice in
// a day. The results live in memory and in one file; the Trivy CLI is the only thing that touches the network.

import { type Counts, emptyCounts, type Finding, hasResults, summarise } from './report.ts';
import type { ResultStore, StoredImage } from './store.ts';
import { type DatabaseInfo, type Reason, ScanFailure, type TrivyRunner } from './trivy.ts';

export type ImageState = 'scanned' | 'queued' | 'scanning' | 'failed';

export interface ImageResult {
  /** What was scanned: `image` today (an SBOM of a release later). The pair kind and ref names a result. */
  kind: 'image';
  ref: string;
  state: ImageState;
  reason?: Reason;
  scannedAt?: string;
  counts: Counts;
  fixable: Counts;
  findings: Finding[];
}

export interface ScanResults {
  generatedAt: string;
  database: DatabaseInfo;
  images: ImageResult[];
}

export interface ScannerOptions {
  rescanAfterMs: number;
  forgetAfterMs: number;
  /** A scan that failed for no lasting reason (a timeout, an error) is tried again after this. */
  retryAfterMs?: number;
  now?: () => number;
  log?: (message: string) => void;
}

const lastingFailures: Reason[] = ['not-found', 'unauthorized', 'unsupported'];

export class Scanner {
  private readonly runner: TrivyRunner;
  private readonly store: ResultStore;
  private readonly options: Required<ScannerOptions>;
  private records = new Map<string, StoredImage>();
  private database: DatabaseInfo = {};
  private queue: string[] = [];
  private current: string | undefined;
  private working: Promise<void> | undefined;
  private saving: Promise<void> = Promise.resolve();

  constructor(runner: TrivyRunner, store: ResultStore, options: ScannerOptions) {
    this.runner = runner;
    this.store = store;
    this.options = {
      retryAfterMs: 3_600_000,
      now: () => Date.now(),
      log: () => undefined,
      ...options,
    };
  }

  async init(): Promise<void> {
    const stored = await this.store.load();
    this.database = stored.database;
    this.records = new Map(stored.records.map((record) => [record.image, record]));
  }

  /** Queues the images that are new or due; returns how many. Images Portarr names stay known for the forget delay. */
  request(images: readonly string[]): number {
    const now = this.options.now();
    const due: { image: string; since: number }[] = [];
    for (const image of new Set(images)) {
      const record = this.records.get(image) ?? { image, requestedAt: now };
      record.requestedAt = now;
      this.records.set(image, record);
      if (image !== this.current && !this.queue.includes(image) && this.isDue(record, now)) {
        due.push({ image, since: record.attemptedAt ?? 0 });
      }
    }
    // Images never scanned go first, then the ones scanned longest ago.
    due.sort((a, b) => a.since - b.since);
    this.queue.push(...due.map((entry) => entry.image));
    this.forget(now);
    this.start();
    if (due.length === 0) {
      void this.persist();
    }
    return due.length;
  }

  results(): ScanResults {
    const now = this.options.now();
    const images = new Map<string, ImageResult>();
    for (const record of this.records.values()) {
      images.set(record.image, this.resultOf(record));
    }
    return {
      generatedAt: new Date(now).toISOString(),
      database: this.database,
      images: [...images.values()].sort((a, b) => a.ref.localeCompare(b.ref)),
    };
  }

  /** Resolves when the queue is empty; the tests wait on it. */
  async idle(): Promise<void> {
    while (this.working) {
      await this.working;
    }
    await this.saving;
  }

  private isDue(record: StoredImage, now: number): boolean {
    if (record.attemptedAt === undefined) {
      return true;
    }
    const wait =
      record.state === 'failed' && !lastingFailures.includes(record.reason as Reason)
        ? this.options.retryAfterMs
        : this.options.rescanAfterMs;
    return now - record.attemptedAt >= wait;
  }

  private resultOf(record: StoredImage): ImageResult {
    // An image with a result keeps showing it while it waits for a rescan; the others say where they are in the queue.
    return {
      kind: 'image',
      ref: record.image,
      state: record.state ?? (record.image === this.current ? 'scanning' : 'queued'),
      ...(record.reason === undefined ? {} : { reason: record.reason }),
      ...(record.scannedAt === undefined ? {} : { scannedAt: record.scannedAt }),
      counts: record.counts ?? emptyCounts(),
      fixable: record.fixable ?? emptyCounts(),
      findings: record.findings ?? [],
    };
  }

  private forget(now: number): void {
    for (const [image, record] of this.records) {
      if (
        now - record.requestedAt > this.options.forgetAfterMs &&
        image !== this.current &&
        !this.queue.includes(image)
      ) {
        this.records.delete(image);
      }
    }
  }

  private start(): void {
    if (this.working || this.queue.length === 0) {
      return;
    }
    this.working = this.work().finally(() => {
      this.working = undefined;
      // Images queued while the worker was finishing are not left waiting.
      this.start();
    });
  }

  private async work(): Promise<void> {
    for (let image = this.queue.shift(); image !== undefined; image = this.queue.shift()) {
      this.current = image;
      await this.scanOne(image);
      this.current = undefined;
    }
  }

  private async scanOne(image: string): Promise<void> {
    const record = this.records.get(image);
    if (record === undefined) {
      return;
    }
    try {
      const report = await this.runner.scan(image);
      if (!hasResults(report)) {
        // Nothing Trivy can read: an image from scratch, or an operating system it does not know.
        throw new ScanFailure('unsupported');
      }
      Object.assign(record, summarise(report), {
        state: 'scanned',
        reason: undefined,
        scannedAt: new Date(this.options.now()).toISOString(),
      });
    } catch (error) {
      const reason: Reason = error instanceof ScanFailure ? error.reason : 'error';
      this.options.log(`scan of ${image} failed: ${reason}`);
      Object.assign(record, {
        state: 'failed',
        reason,
        scannedAt: undefined,
        counts: undefined,
        fixable: undefined,
        findings: undefined,
      });
    }
    record.attemptedAt = this.options.now();
    await this.cleanUp();
    await this.persist();
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
