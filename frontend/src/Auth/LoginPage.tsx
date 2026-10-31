import { type FormEvent, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import AuthBrand from './AuthBrand';
import styles from './AuthPage.module.css';
import { useLogin } from './useAuth';

/** The sign-in page (V7, D-92): shown instead of the app until a session exists. One generic message for every failure. */
export default function LoginPage() {
  const login = useLogin();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [remember, setRemember] = useState(false);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    login.mutate({ username: username.trim(), password, remember });
  };

  const message = (() => {
    if (!login.error) {
      return null;
    }
    if (login.error instanceof ApiError && login.error.status === 429) {
      return translate('LoginTooManyAttempts');
    }
    return login.error instanceof ApiError && login.error.status === 401
      ? translate('LoginFailed')
      : translate('LoginUnavailable');
  })();

  return (
    <main className={styles.screen}>
      <div className={styles.card}>
        <AuthBrand />
        <form className={styles.form} onSubmit={onSubmit}>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="login-username">
              {translate('Username')}
            </label>
            <input
              autoCapitalize="off"
              autoComplete="username"
              autoFocus
              className={styles.input}
              id="login-username"
              maxLength={64}
              onChange={(event) => setUsername(event.target.value)}
              required
              spellCheck={false}
              type="text"
              value={username}
            />
          </div>
          <div className={styles.field}>
            <label className={styles.label} htmlFor="login-password">
              {translate('Password')}
            </label>
            <input
              autoComplete="current-password"
              className={styles.input}
              id="login-password"
              maxLength={128}
              onChange={(event) => setPassword(event.target.value)}
              required
              type="password"
              value={password}
            />
          </div>
          <label className={styles.remember}>
            <input checked={remember} onChange={(event) => setRemember(event.target.checked)} type="checkbox" />
            {translate('RememberMe')}
          </label>
          {message ? <Alert kind="danger">{message}</Alert> : null}
          <button className={styles.submit} disabled={!username.trim() || !password || login.isPending} type="submit">
            {translate('SignIn')}
          </button>
        </form>
      </div>
    </main>
  );
}
