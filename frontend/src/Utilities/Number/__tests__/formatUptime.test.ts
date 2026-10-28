import formatUptime from 'Utilities/Number/formatUptime';

describe('formatUptime', () => {
  it('splits seconds into days, hours and minutes', () => {
    expect(formatUptime(93_780)).toBe('1d 2h 3m');
  });

  it('never shows a negative duration', () => {
    expect(formatUptime(-5)).toBe('0d 0h 0m');
  });
});
