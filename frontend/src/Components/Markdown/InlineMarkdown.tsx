import type { ReactNode } from 'react';

import styles from './MarkdownView.module.css';

// `code`, **bold** and bare http(s) URLs; everything else is plain text.
const tokenPattern = /(`[^`]+`|\*\*[^*]+\*\*|https?:\/\/[^\s)]+)/g;

interface InlineMarkdownProps {
  text: string;
  /**
   * Show URLs as text, not links. A page of Portarr's own data links its URLs; an AI's answer does not, because a link
   * written by a model that read the catalog can carry catalog data in its query string to a host of someone else's choosing.
   */
  plainUrls?: boolean;
}

export default function InlineMarkdown({ text, plainUrls = false }: Readonly<InlineMarkdownProps>) {
  const parts: ReactNode[] = [];
  let last = 0;
  for (const match of text.matchAll(tokenPattern)) {
    const token = match[0];
    const start = match.index;
    if (start > last) {
      parts.push(text.slice(last, start));
    }
    if (token.startsWith('`')) {
      parts.push(
        <code className={styles.code} key={start}>
          {token.slice(1, -1)}
        </code>,
      );
    } else if (token.startsWith('**')) {
      parts.push(<strong key={start}>{token.slice(2, -2)}</strong>);
    } else if (plainUrls) {
      parts.push(<span key={start}>{token}</span>);
    } else {
      parts.push(
        <a href={token} key={start} rel="noreferrer" target="_blank">
          {token}
        </a>,
      );
    }
    last = start + token.length;
  }
  if (last < text.length) {
    parts.push(text.slice(last));
  }
  return <>{parts}</>;
}
