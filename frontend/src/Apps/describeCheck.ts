import type { CheckResult } from 'typings/Check';
import type Status from 'typings/Status';
import translate from 'Utilities/String/translate';

const dayMs = 86_400_000;

export type CheckTone = 'ok' | 'bad' | 'warn' | 'muted';

/** Whole days left before the certificate expires (negative once expired), null without HTTPS. */
export function certificateDaysLeft(result: CheckResult, now: number): number | null {
  return result.certificateExpiresAt ? Math.floor((Date.parse(result.certificateExpiresAt) - now) / dayMs) : null;
}

export function describeCertificate(days: number): string {
  if (days < 0) {
    return translate('CertificateExpired');
  }
  if (days === 0) {
    return translate('CertificateExpiresToday');
  }
  return translate(days === 1 ? 'CertificateExpiresInOneDay' : 'CertificateExpiresInDays', { days });
}

/** "HTTP 502", "Timeout", "Invalid certificate"… from a reason code; unknown codes stay readable. */
export function describeError(error: string): string {
  if (error.startsWith('http-')) {
    return translate('CheckErrorHttp', { code: error.slice(5) });
  }
  const key = `CheckError_${error}`;
  const text = translate(key);
  return text === key ? translate('CheckError_invalid-response') : text;
}

/**
 * The tile line of a check: "200 · 140 ms", "HTTP 502", "Timeout", "Certificate expires in 6 days".
 * A failure wins over the certificate, an expiring certificate over the latency. Whether a certificate is near its
 * expiry is the server's call (D-52): the check's <c>status</c> says Degraded (expiring) or Down (expired).
 */
export default function describeCheck(
  result: CheckResult | null,
  now: number,
  status?: Status,
): { text: string; tone: CheckTone } {
  if (!result) {
    return { text: translate('CheckPending'), tone: 'muted' };
  }
  if (result.error) {
    return { text: describeError(result.error), tone: 'bad' };
  }
  const days = certificateDaysLeft(result, now);
  if (days !== null && (status?.status === 'Degraded' || status?.status === 'Down')) {
    return { text: describeCertificate(days), tone: status.status === 'Down' ? 'bad' : 'warn' };
  }
  return {
    text: translate('CheckUpValue', { code: result.statusCode ?? '', latency: result.latencyMs ?? 0 }),
    tone: 'ok',
  };
}
