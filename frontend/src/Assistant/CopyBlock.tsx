import { useState } from 'react';

import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './CopyBlock.module.css';

interface CopyBlockProps {
  /** What the block holds, for assistive technologies ("Claude Code command"). */
  label: string;
  text: string;
}

/** A command or a configuration snippet, with a button that copies it. */
export default function CopyBlock({ label, text }: Readonly<CopyBlockProps>) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
    } catch {
      // The clipboard needs a secure context (HTTPS or localhost): the text stays selectable.
      setCopied(false);
    }
  };

  return (
    <div className={styles.block}>
      <pre aria-label={label} className={styles.code} tabIndex={0}>
        <code>{text}</code>
      </pre>
      <button className={styles.button} onClick={() => void copy()} type="button">
        <icons.COPY aria-hidden="true" size={14} strokeWidth={2.25} />
        {copied ? translate('Copied') : translate('Copy')}
      </button>
      <span aria-live="polite" className={styles.status}>
        {copied ? translate('CopiedLabel', { label }) : ''}
      </span>
    </div>
  );
}
