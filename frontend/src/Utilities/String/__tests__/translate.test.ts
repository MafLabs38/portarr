import translate from 'Utilities/String/translate';

describe('translate', () => {
  it('returns the English text of a key', () => {
    expect(translate('Status')).toBe('Status');
  });

  it('replaces tokens', () => {
    expect(translate('DurationDaysHoursMinutes', { days: 1, hours: 2, minutes: 3 })).toBe('1d 2h 3m');
  });

  it('falls back to the key when it is missing', () => {
    expect(translate('NoSuchKey')).toBe('NoSuchKey');
  });
});
