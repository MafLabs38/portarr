import { useState } from 'react';

import usePermissions from 'Auth/usePermissions';
import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import form from 'Components/Form/Form.module.css';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import { SETTINGS_MANAGE } from 'Helpers/Props/permissions';
import { formatDateTime } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

import styles from './Catalog.module.css';
import { useApprovePending, usePendingChanges, useRejectPending } from './useCatalog';

/** The changes to how a host is reached that an assistant or an editor asked for: an administrator approves or rejects each. */
export default function PendingPanel() {
  const { data, isError } = usePendingChanges();
  const { can } = usePermissions();
  const approve = useApprovePending();
  const reject = useRejectPending();
  const [problem, setProblem] = useState<string | null>(null);
  const mayDecide = can(SETTINGS_MANAGE);

  const decide = async (action: () => Promise<unknown>) => {
    setProblem(null);
    try {
      await action();
    } catch (error) {
      setProblem(error instanceof ApiError && error.detail ? error.detail : translate('CatalogSaveFailed'));
    }
  };

  if (isError) {
    return <Alert kind="danger">{translate('CatalogLoadFailed')}</Alert>;
  }

  return (
    <Card title={translate('CatalogPending')}>
      <p className={form.help}>{translate(mayDecide ? 'CatalogPendingHelp' : 'CatalogPendingNeedsAdmin')}</p>
      {problem ? <Alert kind="danger">{problem}</Alert> : null}
      {!data || data.length === 0 ? (
        <p className={form.help}>{translate('CatalogPendingEmpty')}</p>
      ) : (
        data.map((change) => (
          <div className={styles.pendingItem} key={change.id}>
            <div className={styles.pendingHead}>
              <strong>{`${change.section} / ${change.itemId}`}</strong>
              <span className={form.help}>
                {translate('CatalogAskedBy', {
                  who: change.via ?? change.user ?? change.channel,
                  when: formatDateTime(change.at),
                })}
              </span>
            </div>
            <ul className={styles.errors}>
              {change.changes.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
            <div className={styles.diff}>
              <pre aria-label={translate('CatalogBefore')} className={styles.diffBox}>
                {change.before ? JSON.stringify(change.before, null, 2) : translate('CatalogNew')}
              </pre>
              <pre aria-label={translate('CatalogAfter')} className={styles.diffBox}>
                {change.after ? JSON.stringify(change.after, null, 2) : translate('CatalogRemoved')}
              </pre>
            </div>
            {mayDecide ? (
              <div className={form.actions}>
                <button
                  className={form.save}
                  disabled={approve.isPending}
                  onClick={() => void decide(() => approve.mutateAsync({ id: change.id }))}
                  type="button"
                >
                  {translate('Approve')}
                </button>
                <button
                  className={form.secondary}
                  disabled={reject.isPending}
                  onClick={() => void decide(() => reject.mutateAsync({ id: change.id }))}
                  type="button"
                >
                  {translate('Reject')}
                </button>
              </div>
            ) : null}
          </div>
        ))
      )}
    </Card>
  );
}
