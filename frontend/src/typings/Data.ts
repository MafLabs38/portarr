/** The state of an expected copy, or of an asset (the worst of its copies). */
export type CopyState = 'protected' | 'waiting' | 'unknown' | 'not-set-up' | 'late' | 'failed' | 'missing';

export type Importance = 'critical' | 'important' | 'comfort';

export type CopyTarget = 'local' | 'offsite';

/** What one source (a Backrest plan, a K8up schedule, a CronJob) says of an expected copy. */
export interface DataSourceCheck {
  /** backrest, k8up or cronjob. */
  tool: string;
  environmentId: string;
  reference: string;
  item: string | null;
  state: CopyState;
  /** A reason code (http-403, no-connector, not-found, no-run, not-in-snapshots…). */
  reason: string | null;
  jobName: string | null;
  schedule: string | null;
  repositories: string[];
  lastRunAt: string | null;
  lastResult: 'success' | 'failed' | null;
  lastSuccessAt: string | null;
  snapshotId: string | null;
  /** When a job that never ran fires next (a cron schedule, read in UTC), if known. */
  nextRunAt: string | null;
}

/** One expected copy of an asset. */
export interface DataCopy {
  state: CopyState;
  reason: string | null;
  lastSuccessAt: string | null;
  lastRunAt: string | null;
  lastResult: 'success' | 'failed' | null;
  sizeBytes: number | null;
  /** The operator makes this copy himself. */
  manual: boolean;
  manualLastSuccess: string | null;
  sources: DataSourceCheck[];
  /** For a copy waiting for its first run: the soonest scheduled run, if known. */
  nextRunAt: string | null;
}

export interface DataWhere {
  environmentId: string;
  environmentName: string;
  namespace: string | null;
  workload: string | null;
  claim: string | null;
  path: string | null;
}

/** Data the operator follows (D-77): declared in the catalog, linked to an app. */
export interface DataAsset {
  id: string;
  name: string;
  appId: string;
  /** postgresql, mariadb, influxdb, vault, sqlite-app-config, volume, files, cluster-state… */
  kind: string;
  where: DataWhere;
  importance: Importance;
  /** The accepted loss window. */
  maxAgeSeconds: number;
  state: CopyState;
  /** Null when the catalog does not expect that copy. */
  local: DataCopy | null;
  offsite: DataCopy | null;
}

/** A backup tool read for the data, with the freshness of the reading. */
export interface DataSource {
  tool: string;
  environmentId: string;
  environmentName: string;
  error: string | null;
  jobs: number;
  checkedAt: string;
}

export interface DataSummary {
  followed: number;
  protected: number;
  late: number;
  failed: number;
  missing: number;
  unknown: number;
  /** Late, failed or missing: waiting, unknown and not set up are not counted. */
  atRisk: number;
  criticalAssets: number;
  criticalOffsite: number;
  /** Assets whose job never ran. */
  waiting: number;
  /** Assets whose backup tool is not configured on its environment. */
  notSetUp: number;
  /** Critical assets whose off-site job has not run yet (not in criticalOffsite, not missing). */
  criticalOffsitePending: number;
}

export default interface DataReport {
  summary: DataSummary;
  assets: DataAsset[];
  sources: DataSource[];
}
