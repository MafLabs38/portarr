// Trivy's JSON, reduced to what Portarr shows: counts of every severity, how many have a fix, the worst findings of
// severity HIGH and CRITICAL, cleaned (the titles come from advisory databases: text written by third parties), and the
// ids of every finding, so that Portarr matches all of them against the known exploited list, not only the ones kept.

export interface Counts {
  critical: number;
  high: number;
  medium: number;
  low: number;
  unknown: number;
}

export interface Finding {
  id: string;
  package: string;
  installedVersion: string;
  fixedVersion?: string;
  severity: 'critical' | 'high';
  title?: string;
  url?: string;
}

export interface Summary {
  counts: Counts;
  fixable: Counts;
  findings: Finding[];
  /** The distinct ids of every finding, any severity, sorted: ids only, never text. */
  ids: string[];
  /** More ids than {@link maxIds}: the least severe were left out. */
  idsTruncated: boolean;
}

export const maxFindings = 50;
export const maxIds = 5000;
/** A plain vulnerability id (CVE-2026-1234, GHSA-xxxx-xxxx-xxxx, DLA-1234-1…): anything else is not passed on. */
export const idPattern = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$/;
export const maxTitleLength = 200;
const maxFieldLength = 200;
const maxUrlLength = 500;

type Severity = keyof Counts;
const severities: Severity[] = ['critical', 'high', 'medium', 'low', 'unknown'];

interface TrivyVulnerability {
  VulnerabilityID?: unknown;
  PkgName?: unknown;
  InstalledVersion?: unknown;
  FixedVersion?: unknown;
  Severity?: unknown;
  Title?: unknown;
  PrimaryURL?: unknown;
}

export function emptyCounts(): Counts {
  return { critical: 0, high: 0, medium: 0, low: 0, unknown: 0 };
}

/** Control and format characters (bidirectional overrides, zero-width, line breaks) are dropped; the text is cut. */
export function clean(value: unknown, max: number): string | undefined {
  if (typeof value !== 'string') {
    return undefined;
  }
  const text = value
    .replaceAll(/\p{Cc}/gu, ' ')
    .replaceAll(/\p{Cf}/gu, '')
    .replaceAll(/\s+/gu, ' ')
    .trim();
  const points = [...text];
  return points.length === 0 ? undefined : points.slice(0, max).join('');
}

/** Only an absolute https address, without credentials. */
export function safeUrl(value: unknown): string | undefined {
  if (typeof value !== 'string' || value.length > maxUrlLength) {
    return undefined;
  }
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.username === '' && url.password === '' ? url.href : undefined;
  } catch {
    return undefined;
  }
}

function severityOf(value: unknown): Severity {
  const name = typeof value === 'string' ? value.toLowerCase() : '';
  return severities.find((severity) => severity === name) ?? 'unknown';
}

function* vulnerabilitiesOf(report: unknown): Generator<TrivyVulnerability> {
  const results = (report as { Results?: unknown } | null)?.Results;
  if (!Array.isArray(results)) {
    return;
  }
  for (const result of results as { Vulnerabilities?: unknown }[]) {
    if (Array.isArray(result?.Vulnerabilities)) {
      yield* result.Vulnerabilities as TrivyVulnerability[];
    }
  }
}

/** Whether Trivy found something it can read in the image: a system or application package, or at least their results. */
export function hasResults(report: unknown): boolean {
  const results = (report as { Results?: unknown } | null)?.Results;
  return Array.isArray(results) && results.length > 0;
}

export function summarise(report: unknown): Summary {
  const counts = emptyCounts();
  const fixable = emptyCounts();
  const seen = new Set<string>();
  const findings: Finding[] = [];
  /** Each plain id and the rank of its worst severity (0 is critical). */
  const ids = new Map<string, number>();

  for (const vulnerability of vulnerabilitiesOf(report)) {
    const id = clean(vulnerability.VulnerabilityID, maxFieldLength);
    const name = clean(vulnerability.PkgName, maxFieldLength);
    const installed = clean(vulnerability.InstalledVersion, maxFieldLength) ?? '';
    if (id === undefined || name === undefined) {
      continue;
    }
    // The same flaw is reported once per file that carries the package: it is one flaw.
    const key = JSON.stringify([id, name, installed]);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);

    const severity = severityOf(vulnerability.Severity);
    const fixedVersion = clean(vulnerability.FixedVersion, maxFieldLength);
    keepId(ids, id, severities.indexOf(severity));
    counts[severity]++;
    if (fixedVersion !== undefined) {
      fixable[severity]++;
    }
    if (severity === 'critical' || severity === 'high') {
      findings.push({
        id,
        package: name,
        installedVersion: installed,
        ...(fixedVersion === undefined ? {} : { fixedVersion }),
        severity,
        ...optional('title', clean(vulnerability.Title, maxTitleLength)),
        ...optional('url', safeUrl(vulnerability.PrimaryURL)),
      });
    }
  }

  findings.sort(
    (a, b) =>
      severities.indexOf(a.severity) - severities.indexOf(b.severity) ||
      Number(b.fixedVersion !== undefined) - Number(a.fixedVersion !== undefined) ||
      a.id.localeCompare(b.id) ||
      a.package.localeCompare(b.package),
  );
  return { counts, fixable, findings: findings.slice(0, maxFindings), ...idsOf(ids) };
}

function keepId(ids: Map<string, number>, id: string, rank: number): void {
  if (!idPattern.test(id)) {
    return;
  }
  const known = ids.get(id);
  if (known === undefined || rank < known) {
    ids.set(id, rank);
  }
}

/** Beyond the maximum, the most severe ids are kept; the list is sorted by id either way. */
function idsOf(ids: Map<string, number>): Pick<Summary, 'ids' | 'idsTruncated'> {
  const byId = (a: string, b: string): number => (a < b ? -1 : Number(a > b));
  let kept = [...ids.keys()];
  if (kept.length > maxIds) {
    kept = kept.toSorted((a, b) => (ids.get(a) ?? 0) - (ids.get(b) ?? 0) || byId(a, b)).slice(0, maxIds);
  }
  return { ids: kept.toSorted(byId), idsTruncated: ids.size > maxIds };
}

function optional<K extends string>(key: K, value: string | undefined): Partial<Record<K, string>> {
  return value === undefined ? {} : ({ [key]: value } as Record<K, string>);
}
