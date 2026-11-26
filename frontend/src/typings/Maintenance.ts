// GET /api/v1/maintenance (V4, D-64 to D-67).

export type ScanningState = 'on' | 'off' | 'unknown';

/** One open alert (D-130). `severity` is null for a secret or a code quality rule. A secret's value is never carried. */
export interface Alert {
  source: 'dependabot' | 'code-scanning' | 'secret-scanning';
  id: string;
  severity: 'critical' | 'high' | 'medium' | 'low' | null;
  title: string;
  package: string | null;
  ecosystem: string | null;
  vulnerableRange: string | null;
  fixedVersion: string | null;
  advisoryId: string | null;
  cveId: string | null;
  /** The file (and line) a code alert points at, or the manifest of a dependency. */
  location: string | null;
  url: string | null;
  createdAt: string | null;
}

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
  /** The worst open alerts (at most 50, worst first); `total` may be larger. */
  alerts: Alert[];
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
  /** The vulnerabilities of the latest release, read from its SBOM (D-132); null when there is no release to scan. */
  releaseVulnerabilities: ReleaseVulnerabilities | null;
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

/** `off`: no scanner declared; `queued`: asked, not scanned yet; `not-scanned`: an image the scanner cannot read (see the reason). */
export type VulnerabilityState = 'off' | 'scanned' | 'queued' | 'failed' | 'not-scanned';

/** `critical`, `high`, `medium`, `low`; null when the database did not rate the vulnerability. */
export type FindingSeverity = 'critical' | 'high' | 'medium' | 'low';

export interface VulnerabilityCounts {
  critical: number;
  high: number;
  medium: number;
  low: number;
  unknown: number;
  total: number;
}

/** One of the worst vulnerabilities of an image. `title` is third-party text: render it as plain text only. */
export interface ImageFinding {
  id: string;
  package: string;
  installedVersion: string;
  /** Null: no fix exists yet. */
  fixedVersion: string | null;
  severity: FindingSeverity | null;
  title: string | null;
  /** An https address, or null. */
  url: string | null;
  /** In CISA's catalog of vulnerabilities known to be exploited in the wild (D-133); null when not. */
  exploited: KnownExploited | null;
  /** EPSS: the probability (0 to 1) that it is exploited in the next 30 days, and its percentile; null when unknown. */
  epss: ExploitPrediction | null;
}

export interface KnownExploited {
  dateAdded: string;
  dueDate: string | null;
  knownRansomwareUse: boolean;
}

export interface ExploitPrediction {
  score: number;
  percentile: number;
}

/** The threat intelligence feeds (D-133): CISA's known exploited catalog and EPSS, read once a day. */
export interface ThreatIntel {
  configured: boolean;
  readAt: string | null;
  error: string | null;
  kevUpdatedAt: string | null;
  epssScoreDate: string | null;
}

export interface ImageVulnerabilities {
  state: VulnerabilityState;
  /** `not-scanned`: image-id, not-found, unauthorized, unsupported. `failed`: timeout, error, or the code of an unreadable scanner. */
  reason: string | null;
  scannedAt: string | null;
  /** Every severity, even below the threshold. */
  counts: VulnerabilityCounts;
  /** The ones that have a fix. */
  fixable: VulnerabilityCounts;
  /** At most 50, worst first, none below the threshold. */
  findings: ImageFinding[];
}

/** The vulnerabilities of the latest release of a repository (D-132): the same facts as an image's, plus the build they describe. */
export interface ReleaseVulnerabilities extends ImageVulnerabilities {
  /** The release tag that was scanned; null until it is. */
  version: string | null;
  /** The repository the release was read from (not the repository's own when the catalog maps it). */
  releaseRepository: string;
}

/** The image scanner companion (D-131): declared in the catalog or not, when it was read, its error code, its database's age. */
export interface ImageScanner {
  configured: boolean;
  readAt: string | null;
  error: string | null;
  databaseUpdatedAt: string | null;
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
  vulnerabilities: ImageVulnerabilities;
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
  /** Images with a scan result. */
  imagesScanned: number;
  /** Images with a critical or high vulnerability that has a fix. */
  imagesVulnerable: number;
  /** Every critical vulnerability of the scanned images, fixed or not. */
  imageCritical: number;
  /** Every high vulnerability of the scanned images, fixed or not. */
  imageHigh: number;
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
  /** Why a date may be missing: `ok`, `pending` (not read yet), `not-listed` (no source lists it), `failed` (the lookup failed) or `disabled`. */
  lookup: 'ok' | 'pending' | 'not-listed' | 'failed' | 'disabled';
}

export default interface MaintenanceReport {
  summary: MaintenanceSummary;
  attention: AttentionItem[];
  codeSource: CodeSource;
  repositories: Repository[];
  images: DeployedImage[];
  refresh: MaintenanceRefresh;
  platforms: Platform[];
  imageScanner: ImageScanner;
  threatIntel: ThreatIntel;
}
