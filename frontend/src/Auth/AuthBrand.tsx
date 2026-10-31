import { useState } from 'react';

import PortholeLogo from 'Components/Page/PortholeLogo';
import translate from 'Utilities/String/translate';

import styles from './AuthPage.module.css';
import { useAuthStatus } from './useAuth';

/** The portal's logo and name at the top of the login and setup pages (the only things shown before signing in). */
export default function AuthBrand() {
  const { data } = useAuthStatus();
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const name = data?.name ?? translate('ProductName');
  const logoUrl = data?.logoUrl && data.logoUrl !== failedUrl ? data.logoUrl : null;

  return (
    <div className={styles.brand}>
      {logoUrl ? (
        <img alt="" className={styles.logo} onError={() => setFailedUrl(logoUrl)} src={logoUrl} />
      ) : (
        <PortholeLogo size={64} />
      )}
      <h1 className={styles.name}>{name}</h1>
    </div>
  );
}
