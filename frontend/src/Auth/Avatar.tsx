import { useState } from 'react';

import styles from './Avatar.module.css';

interface AvatarProps {
  username: string;
  displayName: string;
  avatarUrl: string | null;
  size: number;
}

// Theme tokens only (DESIGN.md §6): a user keeps the same color, derived from the user name.
const tokens = ['accent', 'secondary', 'purple', 'pink', 'infoColor', 'successColor', 'warningColor'];

export function initialsOf(displayName: string, username: string): string {
  const words = (displayName.trim() || username).split(/\s+/).filter(Boolean);
  const letters = words.length > 1 ? `${words[0]?.[0] ?? ''}${words.at(-1)?.[0] ?? ''}` : (words[0] ?? '').slice(0, 2);

  return letters.toUpperCase();
}

function tokenOf(username: string): string {
  let hash = 0;
  for (const character of username.toLowerCase()) {
    hash = (hash * 31 + (character.codePointAt(0) ?? 0)) % 997;
  }

  return tokens[hash % tokens.length] ?? 'accent';
}

/** The round picture of a user, like an account button: the uploaded image clipped to a circle, else their initials. */
export default function Avatar({ username, displayName, avatarUrl, size }: Readonly<AvatarProps>) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const url = avatarUrl && avatarUrl !== failedUrl ? avatarUrl : null;

  return (
    <span
      aria-hidden="true"
      className={styles.avatar}
      style={{
        width: size,
        height: size,
        fontSize: Math.round(size * 0.4),
        backgroundColor: url ? undefined : `var(--${tokenOf(username)})`,
      }}
    >
      {url ? (
        <img alt="" className={styles.image} onError={() => setFailedUrl(url)} src={url} />
      ) : (
        initialsOf(displayName, username)
      )}
    </span>
  );
}
