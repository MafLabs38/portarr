const units = ['B', 'KB', 'MB', 'GB', 'TB'];

/** Human-readable binary size, as `docker stats` shows it: 132 MB, 1.5 GB. */
export default function formatBytes(bytes: number): string {
  let value = Math.max(0, bytes);
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  const digits = value >= 100 || unit === 0 ? 0 : 1;
  return `${value.toFixed(digits)} ${units[unit]}`;
}
