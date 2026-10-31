import { useMemo } from 'react';

import MarkdownView from 'Components/Markdown/MarkdownView';
import type { Knowledge } from 'typings/Assistant';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './AssistantIndex.module.css';

interface Section {
  title: string;
  body: string;
}

/** Splits the document on its "## " headings: the intro, then one collapsible section each. */
function splitSections(markdown: string): { intro: string; sections: Section[] } {
  const [head = '', ...rest] = markdown.split(/^## /m);
  const intro = head.replace(/^# .*$/m, '').trim();
  const sections = rest.map((chunk) => {
    const newline = chunk.indexOf('\n');
    return newline < 0
      ? { title: chunk.trim(), body: '' }
      : { title: chunk.slice(0, newline).trim(), body: chunk.slice(newline + 1) };
  });
  return { intro, sections };
}

/** What an assistant learns when it reads the knowledge base: the same Markdown, rendered. */
export default function KnowledgePreview({ knowledge }: Readonly<{ knowledge: Knowledge }>) {
  const { intro, sections } = useMemo(() => splitSections(knowledge.markdown), [knowledge.markdown]);

  return (
    <div className={styles.knowledge}>
      <p className={styles.text}>
        {translate('KnowledgeHint', { uri: knowledge.uri, age: formatAge(knowledge.generatedAt) })}
      </p>
      <MarkdownView markdown={intro} />
      {sections.map((section, index) => (
        <details className={styles.section} key={section.title} open={index === 0}>
          <summary className={styles.summary}>{section.title}</summary>
          <MarkdownView headingLevel={3} markdown={section.body} />
        </details>
      ))}
    </div>
  );
}
