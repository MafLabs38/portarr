import translate from 'Utilities/String/translate';

/** "just now", "12 min ago", "5 h ago", "11 days ago": how old a date is, for backups (hours to weeks). */
export default function formatAge(at: string, now: number = Date.now()): string {
  const minutes = Math.max(0, Math.floor((now - Date.parse(at)) / 60_000));
  if (minutes < 1) {
    return translate('JustNow');
  }
  if (minutes < 60) {
    return translate('MinutesAgo', { count: minutes });
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 48) {
    return translate('HoursAgo', { count: hours });
  }
  return translate('DaysAgo', { count: Math.floor(hours / 24) });
}

/** "30 min", "24 h", "7 days": a duration kept in whole units, for an accepted loss window. */
export function formatWindow(seconds: number): string {
  const hours = seconds / 3_600;
  if (hours < 1) {
    return translate('DurationMinutes', { count: Math.round(seconds / 60) });
  }
  if (hours < 48 || hours % 24 !== 0) {
    return translate('DurationHours', { count: Math.round(hours) });
  }
  return translate('DurationDays', { count: hours / 24 });
}

/** "less than a minute", "18 min", "3 h": how long until a date (a next scan), the mirror of `formatAge`. */
export function formatUntil(at: string, now: number = Date.now()): string {
  const minutes = Math.max(0, Math.ceil((Date.parse(at) - now) / 60_000));
  if (minutes < 1) {
    return translate('LessThanAMinute');
  }
  if (minutes < 60) {
    return translate('DurationMinutes', { count: minutes });
  }
  const hours = Math.round(minutes / 60);
  return hours < 48
    ? translate('DurationHours', { count: hours })
    : translate('DurationDays', { count: Math.round(hours / 24) });
}
