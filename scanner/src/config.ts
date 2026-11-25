// The scanner's settings, from its environment (D-131). The shared secret is read here and never logged.

export interface ScannerConfig {
  host: string;
  port: number;
  /** Shared with Portarr (PORTARR_SCANNER_SECRET in the catalog's variable): every route but /healthz carries it as a bearer token. */
  secret: string;
  dataDir: string;
  trivyCommand: string;
  /** One scan may run this long; a bigger image is reported as `timeout`. */
  scanTimeoutMs: number;
  /** An image scanned (or attempted) less recently than this is scanned again when Portarr names it. */
  rescanAfterMs: number;
  /** An image Portarr has not named for this long is forgotten. */
  forgetAfterMs: number;
  /** The most targets (images and releases) tracked at once; a new one beyond it is ignored. */
  maxImages: number;
  /** Optional GitHub token, only to lift the anonymous rate limit of the releases API. Never logged. */
  githubToken?: string;
}

export const minimumSecretLength = 32;

/** The most targets (images and releases) the scanner tracks unless PORTARR_SCANNER_MAX_IMAGES says otherwise: far above a homelab's images, far below a flood. */
export const defaultMaxImages = 2000;

function integer(name: string, value: string | undefined, fallback: number, min: number, max: number): number {
  const parsed = value ? Number.parseInt(value, 10) : fallback;
  if (!Number.isInteger(parsed) || parsed < min || parsed > max) {
    throw new Error(`${name} must be an integer between ${min} and ${max}, got '${value}'.`);
  }
  return parsed;
}

export function loadConfig(env: NodeJS.ProcessEnv): ScannerConfig {
  const secret = env.PORTARR_SCANNER_SECRET ?? '';
  if (secret.trim().length < minimumSecretLength) {
    throw new Error(
      `PORTARR_SCANNER_SECRET must be at least ${minimumSecretLength} characters (openssl rand -hex 32).`,
    );
  }
  const githubToken = env.PORTARR_SCANNER_GITHUB_TOKEN?.trim();
  return {
    host: env.PORTARR_SCANNER_HOST ?? '0.0.0.0',
    port: integer('PORTARR_SCANNER_PORT', env.PORTARR_SCANNER_PORT, 8489, 1, 65535),
    secret,
    dataDir: env.PORTARR_SCANNER_DATA ?? '/data',
    trivyCommand: env.TRIVY_BIN ?? 'trivy',
    scanTimeoutMs: integer('PORTARR_SCANNER_TIMEOUT_MINUTES', env.PORTARR_SCANNER_TIMEOUT_MINUTES, 10, 1, 120) * 60_000,
    rescanAfterMs:
      integer('PORTARR_SCANNER_RESCAN_HOURS', env.PORTARR_SCANNER_RESCAN_HOURS, 24, 1, 24 * 30) * 3_600_000,
    forgetAfterMs: integer('PORTARR_SCANNER_FORGET_DAYS', env.PORTARR_SCANNER_FORGET_DAYS, 7, 1, 365) * 86_400_000,
    maxImages: integer('PORTARR_SCANNER_MAX_IMAGES', env.PORTARR_SCANNER_MAX_IMAGES, defaultMaxImages, 10, 10_000),
    ...(githubToken ? { githubToken } : {}),
  };
}
