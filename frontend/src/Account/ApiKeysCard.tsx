import { type FormEvent, useState } from 'react';

import Card from 'Components/Card';
import styles from 'Components/Form/Form.module.css';
import tableStyles from 'Components/Table/Table.module.css';
import * as icons from 'Helpers/Props/icons';
import type { CreatedApiKey } from 'typings/Auth';
import { formatDateTime } from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';

import layout from './Account.module.css';
import { useApiKeys, useCreateApiKey, useRevokeApiKey } from './useAccount';

/**
 * The user's API keys (V7, D-97): named, read-only (the view permissions of the role) or full (all of its permissions), a
 * key never exceeds the role. The key is shown once, when it is created; only its prefix is listed afterwards.
 */
/** When a key stops working, or that it never does. */
function expiryText(expires: string | null): string {
  return expires ? formatDateTime(expires) : translate('Never');
}

export default function ApiKeysCard() {
  const keys = useApiKeys();
  const create = useCreateApiKey();
  const revoke = useRevokeApiKey();
  const [name, setName] = useState('');
  const [scope, setScope] = useState<'read' | 'full'>('read');
  const [expires, setExpires] = useState('');
  const [created, setCreated] = useState<CreatedApiKey | null>(null);
  const [failed, setFailed] = useState(false);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setFailed(false);
    try {
      setCreated(
        await create.mutateAsync({
          name: name.trim(),
          scope,
          expires: expires ? new Date(`${expires}T23:59:59`).toISOString() : undefined,
        }),
      );
      setName('');
      setExpires('');
    } catch {
      setFailed(true);
    }
  };

  return (
    <Card title={translate('ApiKeys')}>
      <p className={styles.help}>{translate('ApiKeysHelpText')}</p>
      {created ? (
        <div className={layout.created} role="status">
          <strong>{translate('ApiKeyCreated', { name: created.key.name })}</strong>
          <code className={layout.secret}>{created.value}</code>
          <span className={styles.help}>{translate('ApiKeyShownOnce')}</span>
        </div>
      ) : null}
      <form className={styles.form} onSubmit={onSubmit}>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="key-name">
            {translate('Name')}
          </label>
          <input
            className={styles.input}
            id="key-name"
            maxLength={60}
            onChange={(event) => setName(event.target.value)}
            required
            type="text"
            value={name}
          />
        </div>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="key-scope">
            {translate('KeyAccess')}
          </label>
          <select
            className={styles.input}
            id="key-scope"
            onChange={(event) => setScope(event.target.value as 'read' | 'full')}
            value={scope}
          >
            <option value="read">{translate('KeyScopeRead')}</option>
            <option value="full">{translate('KeyScopeFull')}</option>
          </select>
        </div>
        <div className={styles.field}>
          <label className={styles.label} htmlFor="key-expires">
            {translate('KeyExpires')}
          </label>
          <input
            className={styles.input}
            id="key-expires"
            onChange={(event) => setExpires(event.target.value)}
            type="date"
            value={expires}
          />
        </div>
        <div className={styles.actions}>
          <button className={styles.save} disabled={!name.trim() || create.isPending} type="submit">
            {translate('CreateKey')}
          </button>
        </div>
        {failed ? (
          <p className={styles.error} role="alert">
            {translate('ApiKeyCreateFailed')}
          </p>
        ) : null}
      </form>

      <div className={tableStyles.wrapper}>
        <table aria-label={translate('ApiKeys')} className={tableStyles.table}>
          <thead>
            <tr>
              <th>{translate('Name')}</th>
              <th>{translate('KeyAccess')}</th>
              <th>{translate('LastUsed')}</th>
              <th>{translate('KeyExpires')}</th>
              <th>
                <span className={tableStyles.visuallyHidden}>{translate('Actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(keys.data ?? []).map((key) => (
              <tr key={key.id}>
                <td>
                  <div className={tableStyles.names}>
                    <span>{key.name}</span>
                    <span className={`${tableStyles.mono} ${tableStyles.muted}`}>{key.prefix}…</span>
                  </div>
                </td>
                <td>{translate(key.scope === 'full' ? 'KeyScopeFullShort' : 'KeyScopeReadShort')}</td>
                <td className={tableStyles.muted}>
                  {key.lastUsed ? formatDateTime(key.lastUsed) : translate('Never')}
                </td>
                <td className={tableStyles.muted}>
                  {key.revoked ? (
                    <span className={`${tableStyles.badge} ${tableStyles.warning}`}>{translate('Revoked')}</span>
                  ) : (
                    expiryText(key.expires)
                  )}
                </td>
                <td>
                  <div className={tableStyles.actions}>
                    {key.active ? (
                      <button
                        aria-label={translate('RevokeKey', { name: key.name })}
                        className={tableStyles.iconButton}
                        disabled={revoke.isPending}
                        onClick={() => revoke.mutate({ id: key.id })}
                        title={translate('Revoke')}
                        type="button"
                      >
                        <icons.DELETE aria-hidden="true" size={16} strokeWidth={2.25} />
                      </button>
                    ) : null}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
