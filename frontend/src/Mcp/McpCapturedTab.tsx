import Alert from 'Components/Alert/Alert';
import type { McpCaptureGroup } from 'typings/Mcp';
import translate from 'Utilities/String/translate';

import McpCaptureCard from './McpCaptureCard';
import styles from './McpCapturedTab.module.css';

interface McpCapturedTabProps {
  groups: McpCaptureGroup[] | undefined;
  isError: boolean;
}

/**
 * The inbox of captured servers (D-116): what the assistants handed over from the machines, each compared with the catalog. A person
 * decides: add, update, keep the catalog's version, or dismiss. Nothing here reaches the catalog until then.
 */
export default function McpCapturedTab({ groups, isError }: Readonly<McpCapturedTabProps>) {
  return (
    <>
      <p className={styles.intro}>{translate('McpCapturedIntro')}</p>

      {isError ? <Alert kind="danger">{translate('ErrorLoadingMcpCaptures')}</Alert> : null}
      {groups?.length === 0 ? <Alert kind="info">{translate('McpCapturedEmpty')}</Alert> : null}

      {groups && groups.length > 0 ? (
        <ul aria-label={translate('McpCapturedTab')} className={styles.list}>
          {groups.map((group) => (
            <McpCaptureCard group={group} key={group.key} />
          ))}
        </ul>
      ) : null}
    </>
  );
}
