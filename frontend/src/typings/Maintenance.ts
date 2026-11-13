// GET /api/v1/maintenance (V4, D-64 to D-67).

export type ScanningState = 'on' | 'off' | 'unknown';

export interface AlertScan {
  state: ScanningState;
  critical: number;
  high: number;
  medium: number;
  low: number;
  /** Code scanning quality rules (error, warning, note): no security severity. */
  other: number;
  total: number;
  /** A reason code when the state is unknown. */
  reason: string | null;
}

export interface Release {
  tag: string;
  publishedAt: string | null;
  url: string | null;
  tagOnly: boolean;
}

export interface CiRun {
  workflow: string | null;
  status: string;
  conclusion: string | null;
  failed: boolean;
  at: string | null;
  url: string | null;
}

export interface Repository {
  fullName: string;
  owner: string;
  name: string;
  url: string;
  visibility: string;
  language: string | null;
  description: string | null;
  pushedAt: string | null;
  defaultBranch: string;
  archived: boolean;
  fork: boolean;
  dependabot: AlertScan;
  codeScanning: AlertScan;
  secretScanning: AlertScan;
  release: Release | null;
  dependabotPullRequests: number | null;
  ci: CiRun | null;
  appIds: string[];
  /** Running images built from this repository. */
  images: string[];
  deployed: boolean;
}

export type ImageStatus = 'up-to-date' | 'behind' | 'newer-build' | 'floating' | 'unknown' | 'pending';

export type VersionChange = 'build' | 'patch' | 'minor' | 'major';

export interface ImageUser {
  environmentId: string;
  environmentName: string;
  name: string;
  namespace: string | null;
  appIds: string[];
}

export interface DeployedImage {
  reference: string;
  registry: string;
  repository: string;
  tag: string | null;
  status: ImageStatus;
  current: string | null;
  latest: string | null;
  change: VersionChange | null;
  currentAt: string | null;
  latestAt: string | null;
  reason: string | null;
  /** The current version is the image's OCI version label (a floating tag). */
  fromLabel: boolean;
  users: ImageUser[];
  repositoryFullName: string | null;
}

export type AttentionLevel = 'danger' | 'warning' | 'info';

export interface AttentionSubject {
  label: string;
  url: string | null;
  count: number | null;
}

export interface AttentionItem {
  kind: string;
  level: AttentionLevel;
  count: number;
  subjects: AttentionSubject[];
}

export interface CodeSource {
  configured: boolean;
  owners: string[];
  scannedAt: string | null;
  error: string | null;
}

export interface MaintenanceSummary {
  repositories: number;
  deployedRepositories: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
  qualityAlerts: number;
  secretAlerts: number;
  dependabotOff: number;
  codeScanningOff: number;
  ciFailing: number;
  dependabotPullRequests: number;
  images: number;
  imagesUpToDate: number;
  imagesBehind: number;
  imagesMajor: number;
  imagesFloating: number;
  imagesUnknown: number;
}

/** When the data was read and when it is read next (D-64, D-66), and the state of a manual refresh. */
export interface MaintenanceRefresh {
  codeAt: string | null;
  codeNextAt: string | null;
  registriesAt: string | null;
  /** A manual refresh was accepted and its pass has not finished. */
  refreshing: boolean;
  /** When the next manual refresh is accepted (null: now). */
  availableAt: string | null;
}

export interface Platform {
  environmentId: string;
  environmentName: string;
  machineId: string;
  machine: string;
  /** `os`, `kubernetes` (a plain kubelet), or the distribution the kubelet names (`k3s`, `rke2r`…). */
  kind: string;
  name: string;
  version: string;
  /** The newest version of the same line, when it is newer than the one that runs. */
  newestPatchOfMinor: string | null;
  /** The first version of the next line: a cluster moves one line at a time. */
  nextMinor: string | null;
  /** A day, yyyy-MM-dd: when active support ends. */
  supportedUntil: string | null;
  /** A day, yyyy-MM-dd: when security fixes stop. */
  endOfLife: string | null;
  /** danger: end of life reached; warning: within 90 days of it; info: a newer version exists; null: nothing to say. */
  level: AttentionLevel | null;
}

export default interface MaintenanceReport {
  summary: MaintenanceSummary;
  attention: AttentionItem[];
  codeSource: CodeSource;
  repositories: Repository[];
  images: DeployedImage[];
  refresh: MaintenanceRefresh;
  platforms: Platform[];
}
