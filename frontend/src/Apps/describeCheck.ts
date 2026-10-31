import type { CheckResult } from 'typings/Check';
import translate from 'Utilities/String/translate';

/** Days before expiry under which a certificate degrades the app (D-52, same threshold as the server). */
export const certificateWarningDays = 14;

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
 * A failure wins over the certificate, an expiring certificate over the latency.
 */
export default function describeCheck(result: CheckResult | null, now: number): { text: string; tone: CheckTone } {
  if (!result) {
    return { text: translate('CheckPending'), tone: 'muted' };
  }
  if (result.error) {
    return { text: describeError(result.error), tone: 'bad' };
  }
  const days = certificateDaysLeft(result, now);
  if (days !== null && days < certificateWarningDays) {
    return { text: describeCertificate(days), tone: days < 0 ? 'bad' : 'warn' };
  }
  return {
    text: translate('CheckUpValue', { code: result.statusCode ?? '', latency: result.latencyMs ?? 0 }),
    tone: 'ok',
  };
}
