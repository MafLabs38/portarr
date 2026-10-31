/** A date and time in the reader's own locale and time zone (account pages: last used, expiry, last sign-in…). */
export function formatDateTime(at: string): string {
  return new Date(at).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}
