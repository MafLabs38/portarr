import { type KeyboardEvent, useState } from 'react';

import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './ChatNotices.module.css';

/** Something worth knowing about the chat, said once and quietly: not an alert in the middle of the conversation. */
export interface ChatNotice {
  id: string;
  /** warning: the chat cannot work as expected; info: a limit worth knowing. */
  kind: 'warning' | 'info';
  text: string;
}

/**
 * The chat's notices behind a bell of the panel's header: a dot says there is something, a click opens the list, Escape or a
 * click elsewhere closes it. Nothing at all when there is nothing to say.
 */
export default function ChatNotices({ notices }: Readonly<{ notices: ChatNotice[] }>) {
  const [open, setOpen] = useState(false);
  if (notices.length === 0) {
    return null;
  }

  const warning = notices.some((notice) => notice.kind === 'warning');
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      setOpen(false);
    }
  };

  return (
    // The wrapper only watches for Escape and for the focus leaving; the controls inside are the real buttons.
    // eslint-disable-next-line jsx-a11y/no-static-element-interactions -- a disclosure wrapper, the focusable parts are the button and the list.
    <div
      className={styles.wrap}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setOpen(false);
        }
      }}
      onKeyDown={onKeyDown}
    >
      <button
        aria-controls="chat-notices"
        aria-expanded={open}
        aria-label={translate('ChatNotices')}
        className={open ? `${styles.bell} ${styles.pressed}` : styles.bell}
        onClick={() => setOpen(!open)}
        title={translate('ChatNotices')}
        type="button"
      >
        <icons.NOTICES aria-hidden="true" size={16} strokeWidth={2.25} />
        <span aria-hidden="true" className={warning ? `${styles.dot} ${styles.warning}` : styles.dot} />
      </button>
      {open ? (
        <ul className={styles.list} id="chat-notices">
          {notices.map((notice) => {
            const Icon = notice.kind === 'warning' ? icons.DEGRADED : icons.INFO;
            return (
              <li className={styles.notice} key={notice.id}>
                <Icon
                  aria-hidden="true"
                  className={notice.kind === 'warning' ? styles.warningIcon : styles.infoIcon}
                  size={14}
                  strokeWidth={2.25}
                />
                <span>{notice.text}</span>
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
  );
}
