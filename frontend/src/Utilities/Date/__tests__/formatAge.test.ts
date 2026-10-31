import formatAge, { formatUntil, formatWindow } from 'Utilities/Date/formatAge';

const now = Date.parse('2026-01-31T12:00:00Z');

describe('formatAge', () => {
  it.each([
    ['2026-01-31T11:59:40Z', 'just now'],
    ['2026-01-31T11:48:00Z', '12 min ago'],
    ['2026-01-30T12:00:00Z', '24 h ago'],
    ['2026-01-20T12:00:00Z', '11 days ago'],
  ])('formats %s as %s', (at, expected) => {
    expect(formatAge(at, now)).toBe(expected);
  });
});

describe('formatWindow', () => {
  it.each([
    [1_800, '30 min'],
    [86_400, '24 h'],
    [604_800, '7 days'],
  ])('formats %d s as %s', (seconds, expected) => {
    expect(formatWindow(seconds)).toBe(expected);
  });
});

describe('formatUntil', () => {
  it.each([
    ['2026-01-31T11:59:00Z', 'less than a minute'],
    ['2026-01-31T12:18:00Z', '18 min'],
    ['2026-01-31T15:00:00Z', '3 h'],
    ['2026-02-03T12:00:00Z', '3 days'],
  ])('formats %s as %s', (at, expected) => {
    expect(formatUntil(at, now)).toBe(expected);
  });
});
