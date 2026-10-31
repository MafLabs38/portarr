import { ArcElement, CategoryScale, Chart, Filler, LinearScale, LineElement, PointElement, Tooltip } from 'chart.js';

// Only the pieces the dashboards use: tree-shaken Chart.js (DESIGN.md §1.1).
Chart.register(ArcElement, CategoryScale, Filler, LinearScale, LineElement, PointElement, Tooltip);

/**
 * A design token's value for Chart.js, which draws on a canvas and cannot read CSS variables itself
 * (DESIGN.md §6: no hard-coded colors). Read at render time, so a theme switch applies on the next render.
 */
export function themeColor(token: string): string {
  if (globalThis.window === undefined) {
    return '';
  }

  return getComputedStyle(document.documentElement).getPropertyValue(token).trim();
}

/** A #rrggbb token with transparency (canvas fills cannot use color-mix); other formats are returned as is. */
export function withAlpha(color: string, alpha: number): string {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(color);
  if (!match) {
    return color;
  }

  const [r, g, b] = match.slice(1).map((hex) => Number.parseInt(hex, 16));
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
