import { type ChangeEvent, type FormEvent, useRef, useState } from 'react';

import usePermissions from 'Auth/usePermissions';
import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import { CATALOG_WRITE } from 'Helpers/Props/permissions';
import translate from 'Utilities/String/translate';

import styles from './Identity.module.css';
import { usePortal, useSetPortal, useUploadPortalLogo } from './usePortal';

const ACCEPTED_LOGOS = '.png,.webp,.svg,image/png,image/webp,image/svg+xml';

const errorKeys: Record<number, string> = {
  400: 'IdentityInvalid',
  401: 'IdentitySessionEnded',
  403: 'IdentityNotAllowed',
  409: 'IdentityStale',
};

/** The result of the last save: it worked, or the translation key of why it did not. */
type Outcome = { saved: true } | { error: string };

/**
 * The identity of the portal (D-91): the name and the logo shown at the bottom of the menu. Changing them writes the
 * catalog file, which needs the catalog.write permission (V7, D-92); with writes off, or without the permission, the
 * card says so.
 */
export default function Identity() {
  const { data } = usePortal();
  const { can } = usePermissions();
  const setPortal = useSetPortal();
  const uploadLogo = useUploadPortalLogo();
  const fileInput = useRef<HTMLInputElement>(null);
  const [name, setName] = useState<string | null>(null);
  const [nameColor, setNameColor] = useState<string | null>(null);
  const [versionColor, setVersionColor] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);

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
        });
      }
      if (file) {
        await uploadLogo.mutateAsync({ file });
        setFile(null);
        if (fileInput.current) {
          fileInput.current.value = '';
        }
      }
      setName(null);
      setNameColor(null);
      setVersionColor(null);
      setOutcome({ saved: true });
    } catch (error) {
      setOutcome({ error: errorKeys[error instanceof ApiError ? error.status : 0] ?? 'IdentitySaveFailed' });
    }
  };

  const pending = setPortal.isPending || uploadLogo.isPending;

  return (
    <Card title={translate('Identity')}>
      <p className={styles.help}>{translate('IdentityHelpText')}</p>
      {data.logoProblem ? (
        <Alert kind="warning">{translate('IdentityLogoNotUsed', { reason: data.logoProblem })}</Alert>
      ) : null}
      {data.writable && can(CATALOG_WRITE) ? (
        <form className={styles.form} onSubmit={onSubmit}>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="identity-name">
              {translate('Name')}
            </label>
            <input
              className={styles.input}
              id="identity-name"
              maxLength={40}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setName(event.target.value)}
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
          <div className={styles.actions}>
            <button className={styles.save} disabled={!changed || pending} type="submit">
              {translate('Save')}
            </button>
            {outcome && 'saved' in outcome ? (
              <output className={styles.saved}>{translate('IdentitySaved')}</output>
            ) : null}
          </div>
          {outcome && 'error' in outcome ? <p role="alert">{translate(outcome.error)}</p> : null}
        </form>
      ) : (
        <p className={styles.help}>{translate(data.writable ? 'IdentityNotAllowed' : 'IdentityReadOnly')}</p>
      )}
    </Card>
  );
}
