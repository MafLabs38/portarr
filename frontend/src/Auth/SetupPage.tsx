import { type FormEvent, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import AuthBrand from './AuthBrand';
import styles from './AuthPage.module.css';
import { useSetup } from './useAuth';

export const minimumPasswordLength = 10;

/**
 * The first run (F-SET-07, V7): while no user exists every page comes here to create the first administrator. The server
 * accepts it only while the users table is empty, so the page cannot be used to take over an installed Portarr.
 */
export default function SetupPage() {
  const setup = useSetup();
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');

  const tooShort = password.length > 0 && password.length < minimumPasswordLength;
  const mismatch = confirmation.length > 0 && confirmation !== password;
  const ready = username.trim().length >= 3 && password.length >= minimumPasswordLength && confirmation === password;

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setup.mutate({ username: username.trim(), displayName: displayName.trim() || undefined, password });
  };

  const message = (() => {
    if (!setup.error) {
      return null;
    }
    if (setup.error instanceof ApiError && setup.error.status === 409) {
      return translate('SetupAlreadyDone');
    }
    return setup.error instanceof ApiError && setup.error.status === 400
      ? translate('SetupInvalid')
      : translate('LoginUnavailable');
  })();

  return (
    <main className={styles.screen}>
      <div className={styles.card}>
        <AuthBrand />
        <p className={styles.lead}>{translate('SetupLead')}</p>
        <form className={styles.form} onSubmit={onSubmit}>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="setup-username">
              {translate('Username')}
            </label>
            <input
              aria-describedby="setup-username-help"
              autoCapitalize="off"
              autoComplete="username"
              autoFocus
              className={styles.input}
              id="setup-username"
              maxLength={32}
              onChange={(event) => setUsername(event.target.value)}
              required
              spellCheck={false}
              type="text"
              value={username}
            />
            <span className={styles.help} id="setup-username-help">
              {translate('UsernameHelpText')}
            </span>
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="setup-display-name">
              {translate('DisplayName')}
            </label>
            <input
              className={styles.input}
              id="setup-display-name"
              maxLength={64}
              onChange={(event) => setDisplayName(event.target.value)}
              type="text"
              value={displayName}
            />
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="setup-password">
              {translate('Password')}
            </label>
            <input
              aria-describedby="setup-password-help"
              autoComplete="new-password"
              className={styles.input}
              id="setup-password"
              maxLength={128}
              onChange={(event) => setPassword(event.target.value)}
              required
              type="password"
              value={password}
            />
            <span className={styles.help} id="setup-password-help">
              {tooShort
                ? translate('PasswordTooShort', { count: minimumPasswordLength })
                : translate('PasswordHelpText', { count: minimumPasswordLength })}
            </span>
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="setup-confirmation">
              {translate('ConfirmPassword')}
            </label>
            <input
              autoComplete="new-password"
              className={styles.input}
              id="setup-confirmation"
              maxLength={128}
              onChange={(event) => setConfirmation(event.target.value)}
              required
              type="password"
              value={confirmation}
            />
            {mismatch ? <span className={styles.help}>{translate('PasswordsDoNotMatch')}</span> : null}
          </div>
          {message ? <Alert kind="danger">{message}</Alert> : null}
          <button className={styles.submit} disabled={!ready || setup.isPending} type="submit">
            {translate('CreateAdministrator')}
          </button>
        </form>
      </div>
    </main>
  );
}
