import type { ReactNode } from 'react';

import Alert from 'Components/Alert/Alert';
import { ApiError } from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';

import styles from './AuthPage.module.css';
import LoginPage from './LoginPage';
import SetupPage from './SetupPage';
import { useAuthStatus, useMe } from './useAuth';

interface AuthGateProps {
  children: ReactNode;
}

/**
 * Nothing of the app shows before a session exists (V7, D-92): the first run goes to the setup page, no session to the
 * login page, and a session 401 anywhere (it ended) comes back here. The API enforces everything again; this only
 * decides what the browser shows.
 */
export default function AuthGate({ children }: Readonly<AuthGateProps>) {
  const status = useAuthStatus();
  const me = useMe();

  if (status.isPending || me.isPending) {
    return (
      <main className={styles.screen}>
        <p>{translate('Loading')}</p>
      </main>
    );
  }

  if (status.data?.setupRequired) {
    return <SetupPage />;
  }

  if (me.isError) {
    return me.error instanceof ApiError && me.error.status === 401 ? (
      <LoginPage />
    ) : (
      <main className={styles.screen}>
        <Alert kind="danger">{translate('LoginUnavailable')}</Alert>
      </main>
    );
  }

  return <>{children}</>;
}
