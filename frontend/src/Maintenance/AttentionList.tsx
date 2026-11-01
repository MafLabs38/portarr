import type { LucideIcon } from 'lucide-react';
import { Link } from 'react-router-dom';

import * as icons from 'Helpers/Props/icons';
import type { AttentionItem, AttentionLevel } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import styles from './AttentionList.module.css';
import { attentionSearch } from './maintenanceFilters';

const levelIcons: Record<AttentionLevel, LucideIcon> = {
  danger: icons.VULNERABILITY,
  warning: icons.DEGRADED,
  info: icons.INFO,
};

/** Subjects named before "+N more": enough to act on, short enough to scan. */
const shownSubjects = 8;

function describe(item: AttentionItem): string {
  if (item.kind === 'code-source-error') {
    return translate('Attention_code-source-error', { reason: translate(`Reason_${item.subjects[0]?.label ?? ''}`) });
  }
  return translate(`Attention_${item.kind}`, { count: item.count, repositories: item.subjects.length });
}

/**
 * "What needs attention", across code and images (D-67): one line per kind of finding, worst first, with
 * what it concerns. The line is a link that filters the page to the items concerned; each subject links to
 * where the operator acts (the GitHub page, the CI run).
 */
export default function AttentionList({ items }: Readonly<{ items: AttentionItem[] }>) {
  if (items.length === 0) {
    return <p className={styles.clear}>{translate('NothingNeedsAttention')}</p>;
  }

  return (
    <ul aria-label={translate('NeedsAttention')} className={styles.list}>
      {items.map((item) => {
        const Icon = levelIcons[item.level];
        const subjects = item.kind === 'code-source-error' ? [] : item.subjects.slice(0, shownSubjects);
        const more = item.kind === 'code-source-error' ? 0 : item.subjects.length - subjects.length;
        const search = attentionSearch(item.kind);
        const what = (
          <>
            <Icon aria-hidden="true" size={16} strokeWidth={2.5} />
            {describe(item)}
          </>
        );
        return (
          <li className={`${styles.item} ${styles[item.level]}`} key={item.kind}>
            {search === null ? (
              <span className={styles.what}>{what}</span>
            ) : (
              <Link className={`${styles.what} ${styles.filter}`} title={translate('ShowOnlyThese')} to={{ search }}>
                {what}
              </Link>
            )}
            <span className={styles.subjects}>
              {subjects.map((subject) => {
                const text = subject.count && subject.count > 1 ? `${subject.label} (${subject.count})` : subject.label;
                return subject.url ? (
                  <a className={styles.subject} href={subject.url} key={subject.label} rel="noreferrer" target="_blank">
                    {text}
                  </a>
                ) : (
                  <span className={styles.subject} key={subject.label}>
                    {text}
                  </span>
                );
              })}
              {more > 0 ? <span className={styles.more}>{translate('AndMore', { count: more })}</span> : null}
            </span>
          </li>
        );
      })}
    </ul>
  );
}
