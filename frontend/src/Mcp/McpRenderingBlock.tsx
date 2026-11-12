import CopyBlock from 'Assistant/CopyBlock';
import type { McpRendering } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import styles from './McpRenderingBlock.module.css';

interface McpRenderingBlockProps {
  rendering: McpRendering;
  /** What heads the block, when the client name alone is not enough (a plan lists several servers). */
  title?: string;
}

/**
 * How to install the server in one client: the command to run and/or the entry to merge into the file, each with a copy button,
 * the variables the machine must provide, and what to know. Shown to the operator; an assistant gets the same through the MCP tool.
 */
export default function McpRenderingBlock({ rendering, title }: Readonly<McpRenderingBlockProps>) {
  const heading = title ?? rendering.clientName;

  return (
    <section aria-label={heading} className={styles.block}>
      <h3 className={styles.heading}>
        {heading}
        <span className={styles.scope}>{translate('McpScopeValue', { scope: rendering.scope })}</span>
      </h3>

      {rendering.supported ? null : <p className={styles.description}>{rendering.notes.join(' ')}</p>}

      {rendering.requiresEnv.length > 0 ? (
        <p className={styles.requires}>
          {translate('McpRequiresEnv')}{' '}
          {rendering.requiresEnv.map((name, index) => (
            <span key={name}>
              {index > 0 ? ', ' : ''}
              <code>{name}</code>
            </span>
          ))}
        </p>
      ) : null}

      {rendering.requiresSecrets.length > 0 ? (
        <p className={styles.requires}>
          {translate('McpRequiresSecrets')}{' '}
          {rendering.requiresSecrets.map((key, index) => (
            <span key={key}>
              {index > 0 ? ', ' : ''}
              <code>{key}</code>
            </span>
          ))}
        </p>
      ) : null}

      {rendering.operations.map((operation) => (
        <div className={styles.operation} key={`${operation.kind}-${operation.path ?? operation.text}`}>
          <p className={styles.description}>{operation.description}</p>
          <CopyBlock label={`${heading}: ${operation.description}`} text={operation.text} />
        </div>
      ))}

      {rendering.supported && rendering.notes.length > 0 ? (
        <ul className={styles.notes}>
          {rendering.notes.map((note) => (
            <li key={note}>{note}</li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
