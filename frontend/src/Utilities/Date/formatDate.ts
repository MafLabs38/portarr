/** A date and time in the reader's own locale and time zone (account pages: last used, expiry, last sign-in…). */
export function formatDateTime(at: string): string {
  return new Date(at).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

/** A calendar day (yyyy-MM-dd, no time zone: a support end is a day, not an instant) in the reader's locale. */
export function formatDay(day: string): string {
  return new Date(`${day}T12:00:00`).toLocaleDateString(undefined, { dateStyle: 'medium' });
}
