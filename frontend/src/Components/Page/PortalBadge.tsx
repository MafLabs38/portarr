import { useState } from 'react';

import usePermissions from 'Auth/usePermissions';
import { usePortal } from 'System/Status/usePortal';
import useSystemStatus from 'System/Status/useSystemStatus';
import translate from 'Utilities/String/translate';

import styles from './PortalBadge.module.css';
import { portalColorToCss } from './portalColor';
import PortholeLogo from './PortholeLogo';

/** The version follows the system status, so a new version shows up without reloading the page. */
const STATUS_REFRESH_MS = 60_000;

/**
 * The identity of this portal at the bottom of the menu (D-91): the name of the infrastructure, its logo
 * (Portarr's own porthole when none is set) and the version of the running Portarr, straight on the sidebar (no
 * card). Only the image is clipped to rounded corners. The texts take the colors the catalog sets (Portarr's accent
 * by default). Small by design; on a small screen only the logo remains, the texts stay available to assistive
 * technology.
 */
export default function PortalBadge() {
  const { data, isPending } = usePortal();
  // Roles without system.view keep the version the portal answer carries.
  const { can } = usePermissions();
  const status = useSystemStatus({ refetchInterval: STATUS_REFRESH_MS, enabled: can('system.view') });
  const [failedUrl, setFailedUrl] = useState<string | null>(null);

  if (isPending) {
    return null;
  }

  const name = data?.name ?? translate('ProductName');
  const logoUrl = data?.logoUrl && data.logoUrl !== failedUrl ? data.logoUrl : null;
  const version = status.data?.version ?? data?.version;
  const versionText = version ? translate('VersionNumber', { version }) : null;

  return (
    <div className={styles.badge} title={name}>
      <span className={styles.name} style={{ color: portalColorToCss(data?.colors.name) }}>
        {name}
      </span>
      {logoUrl ? (
        <img alt={name} className={styles.logo} onError={() => setFailedUrl(logoUrl)} src={logoUrl} />
      ) : (
        <span aria-hidden="true" className={styles.logo}>
          <PortholeLogo size={48} />
        </span>
      )}
      {versionText ? (
        <span className={styles.version} style={{ color: portalColorToCss(data?.colors.version) }}>
          {versionText}
        </span>
      ) : null}
    </div>
  );
}
