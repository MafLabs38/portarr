import type Status from './Status';

export interface CheckResult {
  at: string;
  up: boolean;
  statusCode: number | null;
  latencyMs: number | null;
  /** Reason code: timeout, dns-failure, connection-failed, tls-invalid, tls-expired, keyword-missing, http-<code>… */
  error: string | null;
  certificateExpiresAt: string | null;
}

/** The check of an app, as shown on its tile. */
export interface AppCheck {
  url: string;
  critical: boolean;
  status: Status;
  latest: CheckResult | null;
}
