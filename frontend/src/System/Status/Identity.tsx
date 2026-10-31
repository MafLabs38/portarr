import { type ChangeEvent, type FormEvent, useRef, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import styles from './Identity.module.css';
import { usePortal, useSetPortal, useUploadPortalLogo } from './usePortal';

const ACCEPTED_LOGOS = '.png,.webp,.svg,image/png,image/webp,image/svg+xml';

const errorKeys: Record<number, string> = {
  400: 'IdentityInvalid',
  401: 'IdentityKeyRejected',
  403: 'IdentityReadOnly',
  409: 'IdentityStale',
};

/**
 * The identity of the portal (D-91): the name and the logo shown at the bottom of the menu. Changing them writes the
 * catalog file, which needs the API key until Portarr has accounts (D-85); with writes off the card says how to
 * turn them on.
 */
export default function Identity() {
  const { data } = usePortal();
  const setPortal = useSetPortal();
  const uploadLogo = useUploadPortalLogo();
  const fileInput = useRef<HTMLInputElement>(null);
  const [name, setNameText] = useState<string | null>(null);
  const [nameColor, setNameColor] = useState<string | null>(null);
  const [versionColor, setVersionColor] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [apiKey, setApiKey] = useState('');
  const [outcome, setOutcome] = useState<'saved' | string | null>(null);

  if (!data) {
    return <Card title={translate('Identity')}>{translate('Loading')}</Card>;
  }

  const current = name ?? data.name;
  const currentNameColor = nameColor ?? data.colors.name;
  const currentVersionColor = versionColor ?? data.colors.version;
  const nameChanged = current.trim() !== data.name;
  const colorsChanged =
    currentNameColor.trim() !== data.colors.name || currentVersionColor.trim() !== data.colors.version;
  const changed = nameChanged || colorsChanged || file !== null;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setOutcome(null);
    try {
      if (nameChanged || colorsChanged) {
        await setPortal.mutateAsync({
          name: nameChanged ? current.trim() : undefined,
          colors: colorsChanged ? { name: currentNameColor.trim(), version: currentVersionColor.trim() } : undefined,
          apiKey,
        });
      }
      if (file) {
        await uploadLogo.mutateAsync({ file, apiKey });
        setFile(null);
        if (fileInput.current) {
          fileInput.current.value = '';
        }
      }
      setNameText(null);
      setNameColor(null);
      setVersionColor(null);
      setOutcome('saved');
    } catch (error) {
      setOutcome(errorKeys[error instanceof ApiError ? error.status : 0] ?? 'IdentitySaveFailed');
    }
  };

  const pending = setPortal.isPending || uploadLogo.isPending;

  return (
    <Card title={translate('Identity')}>
      <p className={styles.help}>{translate('IdentityHelpText')}</p>
      {data.logoProblem ? (
        <Alert kind="warning">{translate('IdentityLogoNotUsed', { reason: data.logoProblem })}</Alert>
      ) : null}
      {data.writable ? (
        <form className={styles.form} onSubmit={onSubmit}>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-name">
              {translate('Name')}
            </label>
            <input
              className={styles.input}
              id="identity-name"
              maxLength={40}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setNameText(event.target.value)}
              type="text"
              value={current}
            />
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-name-color">
              {translate('NameColor')}
            </label>
            <input
              aria-describedby="identity-color-help"
              autoCapitalize="off"
              className={styles.input}
              id="identity-name-color"
              maxLength={12}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setNameColor(event.target.value)}
              spellCheck={false}
              type="text"
              value={currentNameColor}
            />
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-version-color">
              {translate('VersionColor')}
            </label>
            <input
              aria-describedby="identity-color-help"
              autoCapitalize="off"
              className={styles.input}
              id="identity-version-color"
              maxLength={12}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setVersionColor(event.target.value)}
              spellCheck={false}
              type="text"
              value={currentVersionColor}
            />
            <span className={styles.help} id="identity-color-help">
              {translate('ColorHelpText')}
            </span>
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-logo">
              {translate('Logo')}
            </label>
            <input
              accept={ACCEPTED_LOGOS}
              aria-describedby="identity-logo-help"
              className={styles.input}
              id="identity-logo"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setFile(event.target.files?.[0] ?? null)}
              ref={fileInput}
              type="file"
            />
            <span className={styles.help} id="identity-logo-help">
              {translate('LogoHelpText')}
            </span>
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-api-key">
              {translate('ApiKey')}
            </label>
            <input
              aria-describedby="identity-api-key-help"
              autoComplete="off"
              className={styles.input}
              id="identity-api-key"
              onChange={(event: ChangeEvent<HTMLInputElement>) => setApiKey(event.target.value)}
              type="password"
              value={apiKey}
            />
            <span className={styles.help} id="identity-api-key-help">
              {translate('ApiKeyHelpText')}
            </span>
          </div>
          <div className={styles.actions}>
            <button className={styles.save} disabled={!changed || !apiKey || pending} type="submit">
              {translate('Save')}
            </button>
            {outcome === 'saved' ? (
              <span className={styles.saved} role="status">
                {translate('IdentitySaved')}
              </span>
            ) : null}
          </div>
          {outcome && outcome !== 'saved' ? <p role="alert">{translate(outcome)}</p> : null}
        </form>
      ) : (
        <p className={styles.help}>{translate('IdentityReadOnly')}</p>
      )}
    </Card>
  );
}
