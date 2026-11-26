// The results, kept as one JSON file in the data folder and replaced atomically: a crash leaves the previous file.

import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import type { TargetKind } from './reference.ts';
import type { Counts, Finding } from './report.ts';
import type { DatabaseInfo, Reason } from './trivy.ts';

/** What the scanner knows of one target (an image, or the latest release of a repository); `queued` and `scanning` are the queue's, never stored. */
export interface StoredImage {
  /** The reference: an image reference, or `owner/name` for a release. Named `image` as in the first files. */
  image: string;
  /** Absent in a file written before releases existed: an image. */
  kind?: TargetKind;
  /** The last time Portarr named the image: an image nobody names for a while is forgotten. */
  requestedAt: number;
  /** When the last scan ended, successful or not: the rescan interval counts from here. */
  attemptedAt?: number;
  state?: 'scanned' | 'failed';
  reason?: Reason;
  scannedAt?: string;
  /** The tag of the release that was scanned. */
  version?: string;
  counts?: Counts;
  fixable?: Counts;
  findings?: Finding[];
}

export interface Stored {
  database: DatabaseInfo;
  records: StoredImage[];
}

export class ResultStore {
  private readonly file: string;
  private readonly dir: string;

  constructor(dataDir: string) {
    this.dir = dataDir;
    this.file = join(dataDir, 'results.json');
  }

  async load(): Promise<Stored> {
    await mkdir(this.dir, { recursive: true, mode: 0o700 });
    try {
      const stored = JSON.parse(await readFile(this.file, 'utf8')) as Partial<Stored>;
      return {
        database: stored.database ?? {},
        records: Array.isArray(stored.records)
          ? stored.records.filter(
              (r) =>
                typeof r?.image === 'string' && (r.kind === undefined || r.kind === 'image' || r.kind === 'release'),
            )
          : [],
      };
    } catch {
      // No file yet, or one that cannot be read: start empty, the images are scanned again.
      return { database: {}, records: [] };
    }
  }

  async save(stored: Stored): Promise<void> {
    const temporary = `${this.file}.tmp`;
    await writeFile(temporary, JSON.stringify(stored), { mode: 0o600 });
    await rename(temporary, this.file);
  }
}
