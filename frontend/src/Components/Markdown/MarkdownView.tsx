import { useMemo } from 'react';

import InlineMarkdown from './InlineMarkdown';
import styles from './MarkdownView.module.css';
import parseMarkdown, { type Block, type ListItem } from './parseMarkdown';

interface MarkdownViewProps {
  markdown: string;
  /** Level of the document's top heading on the page: `#` becomes this level, `##` the next one. */
  headingLevel?: number;
}

function List({ items }: Readonly<{ items: ListItem[] }>) {
  return (
    <ul className={styles.list}>
      {items.map((item, index) => (
        <li key={index}>
          <InlineMarkdown text={item.text} />
          {item.children.length > 0 ? <List items={item.children} /> : null}
        </li>
      ))}
    </ul>
  );
}

function BlockView({ block, headingLevel }: Readonly<{ block: Block; headingLevel: number }>) {
  switch (block.type) {
    case 'heading': {
      const Tag = `h${Math.min(6, headingLevel + block.level - 1)}` as 'h3';
      return (
        <Tag className={styles.heading}>
          <InlineMarkdown text={block.text} />
        </Tag>
      );
    }
    case 'list':
      return <List items={block.items} />;
    case 'table':
      return (
        <div className={styles.tableWrapper}>
          <table className={styles.table}>
            <thead>
              <tr>
                {block.header.map((cell) => (
                  <th key={cell} scope="col">
                    <InlineMarkdown text={cell} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {block.rows.map((row, rowIndex) => (
                <tr key={rowIndex}>
                  {row.map((cell, cellIndex) => (
                    <td key={cellIndex}>
                      <InlineMarkdown text={cell} />
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
          <InlineMarkdown text={block.text} />
        </p>
      );
  }
}

/** Renders Portarr's own Markdown documents (headings, paragraphs, lists, tables) without interpreting any HTML. */
export default function MarkdownView({ markdown, headingLevel = 3 }: Readonly<MarkdownViewProps>) {
  const blocks = useMemo(() => parseMarkdown(markdown), [markdown]);
  return (
    <div className={styles.markdown}>
      {blocks.map((block, index) => (
        <BlockView block={block} headingLevel={headingLevel} key={index} />
      ))}
    </div>
  );
}
