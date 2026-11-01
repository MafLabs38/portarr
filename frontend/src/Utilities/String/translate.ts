import en from './en';

export type TranslateTokens = Record<string, string | number>;

/**
 * Returns the UI text for a key, replacing `{token}` placeholders (same signature as in the *arr apps).
 * Falls back to the key itself so a missing translation is visible but never breaks the page.
 */
export default function translate(key: string, tokens?: TranslateTokens): string {
  const text = en[key] ?? key;

  if (!tokens) {
    return text;
  }

  return text.replaceAll(/\{(\w+)\}/g, (match, name: string) => String(tokens[name] ?? match));
}
