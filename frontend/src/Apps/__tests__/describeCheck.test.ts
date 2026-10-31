import describeCheck from 'Apps/describeCheck';
import type { CheckResult } from 'typings/Check';

const now = Date.UTC(2026, 0, 1, 12, 0);

function result(overrides: Partial<CheckResult> = {}): CheckResult {
  return {
    at: new Date(now).toISOString(),
    up: true,
    statusCode: 200,
    latencyMs: 140,
    error: null,
    certificateExpiresAt: null,
    ...overrides,
  };
}

function inDays(days: number) {
  return new Date(now + days * 86_400_000).toISOString();
}

describe('describeCheck', () => {
  it('shows the status code and the latency of a passing check', () => {
    expect(describeCheck(result(), now)).toEqual({ text: '200 · 140 ms', tone: 'ok' });
  });

  it.each([
    ['http-502', 'HTTP 502'],
    ['timeout', 'Timeout'],
    ['tls-invalid', 'Invalid certificate'],
    ['something-new', 'Unexpected response'],
  ])('names the failure %s', (error, text) => {
    expect(describeCheck(result({ up: false, error }), now)).toEqual({ text, tone: 'bad' });
  });

  it('warns about a certificate expiring within 14 days, before the latency', () => {
    expect(describeCheck(result({ certificateExpiresAt: inDays(6.5) }), now)).toEqual({
      text: 'Certificate expires in 6 days',
      tone: 'warn',
    });
    expect(describeCheck(result({ certificateExpiresAt: inDays(30) }), now).text).toBe('200 · 140 ms');
  });

  it('says a check has not run yet', () => {
    expect(describeCheck(null, now)).toEqual({ text: 'Not checked yet', tone: 'muted' });
  });
});
