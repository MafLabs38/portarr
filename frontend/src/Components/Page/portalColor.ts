/** The theme tokens a badge color may name (D-91); the same list as the server's. */
const TOKENS = ['accent', 'accentDeep', 'secondary', 'ink', 'inkSoft', 'inkMuted'];

const HEX = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;

export const DEFAULT_PORTAL_COLOR = 'accent';

/**
 * The CSS color for a badge color: a hex color as it is, a theme token as its variable. Anything else (the server
 * never sends it) falls back to the accent, so no arbitrary CSS can reach the style.
 */
export function portalColorToCss(value: string | undefined): string {
  if (value && HEX.test(value)) {
    return value;
  }

  return `var(--${value && TOKENS.includes(value) ? value : DEFAULT_PORTAL_COLOR})`;
}
