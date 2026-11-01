import { useMemo } from 'react';

import InlineMarkdown from './InlineMarkdown';
import styles from './MarkdownView.module.css';
import parseMarkdown, { type Block, type ListItem } from './parseMarkdown';

interface MarkdownViewProps {
  markdown: string;
  /** Level of the document's top heading on the page: `#` becomes this level, `##` the next one. */
  headingLevel?: number;
  /** Show URLs as text, not links: for what a model wrote (see InlineMarkdown). */
  plainUrls?: boolean;
}

function List({ items, plainUrls }: Readonly<{ items: ListItem[]; plainUrls: boolean }>) {
  return (
    <ul className={styles.list}>
      {items.map((item, index) => (
        <li key={index}>
          <InlineMarkdown plainUrls={plainUrls} text={item.text} />
          {item.children.length > 0 ? <List items={item.children} plainUrls={plainUrls} /> : null}
        </li>
      ))}
    </ul>
  );
}

function BlockView({
  block,
  headingLevel,
  plainUrls,
}: Readonly<{ block: Block; headingLevel: number; plainUrls: boolean }>) {
  switch (block.type) {
    case 'heading': {
      const Tag = `h${Math.min(6, headingLevel + block.level - 1)}` as 'h3';
      return (
        <Tag className={styles.heading}>
          <InlineMarkdown plainUrls={plainUrls} text={block.text} />
        </Tag>
      );
    }
    case 'list':
      return <List items={block.items} plainUrls={plainUrls} />;
    case 'table':
      return (
        <div className={styles.tableWrapper}>
          <table className={styles.table}>
            <thead>
              <tr>
                {block.header.map((cell) => (
                  <th key={cell} scope="col">
                    <InlineMarkdown plainUrls={plainUrls} text={cell} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {block.rows.map((row, rowIndex) => (
                <tr key={rowIndex}>
                  {row.map((cell, cellIndex) => (
                    <td key={cellIndex}>
                      <InlineMarkdown plainUrls={plainUrls} text={cell} />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    default:
      return (
        <p className={styles.paragraph}>
          <InlineMarkdown plainUrls={plainUrls} text={block.text} />
        </p>
      );
  }
}

/** Renders Portarr's own Markdown documents (headings, paragraphs, lists, tables) without interpreting any HTML. */
export default function MarkdownView({ markdown, headingLevel = 3, plainUrls = false }: Readonly<MarkdownViewProps>) {
  const blocks = useMemo(() => parseMarkdown(markdown), [markdown]);
  return (
    <div className={styles.markdown}>
      {blocks.map((block, index) => (
        <BlockView block={block} headingLevel={headingLevel} key={index} plainUrls={plainUrls} />
      ))}
    </div>
  );
}
