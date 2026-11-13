import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { Platform } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

// The same pill as an image's verdict: one look for "where does this stand".
import styles from './ImageStatusLabel.module.css';

function look(platform: Platform): { kind: kinds.Kind; icon: LucideIcon; text: string } {
  switch (platform.level) {
    case 'danger':
      return { kind: kinds.DANGER, icon: icons.DEGRADED, text: translate('PlatformStatus_out-of-support') };
    case 'warning':
      return { kind: kinds.WARNING, icon: icons.DEGRADED, text: translate('PlatformStatus_support-ending') };
    case 'info':
      return { kind: kinds.INFO, icon: icons.UPDATE, text: translate('PlatformStatus_newer-version') };
    default:
      return noDangerLook(platform);
  }
}

/** Nothing to warn about: supported, or why the support date is missing (a failed lookup is a warning, never a neutral grey). */
function noDangerLook(platform: Platform): { kind: kinds.Kind; icon: LucideIcon; text: string } {
  if (platform.lookup === 'failed') {
    return { kind: kinds.WARNING, icon: icons.DEGRADED, text: translate('PlatformStatus_lookup-failed') };
  }
  if (platform.lookup === 'disabled') {
    return { kind: kinds.DISABLED, icon: icons.UNKNOWN, text: translate('PlatformStatus_lookups-off') };
  }
  if (platform.endOfLife) {
    return { kind: kinds.SUCCESS, icon: icons.HEALTHY, text: translate('PlatformStatus_supported') };
  }
  const text = translate(platform.lookup === 'pending' ? 'PlatformStatus_checking' : 'PlatformStatus_unknown');
  return { kind: kinds.DISABLED, icon: icons.UNKNOWN, text };
}

/** Where a platform stands: icon plus text, never color alone. */
export default function PlatformStatusLabel({ platform }: Readonly<{ platform: Platform }>) {
  const { kind, icon: Icon, text } = look(platform);
  return (
    <span className={`${styles.label} ${styles[kind]}`}>
      <Icon aria-hidden="true" size={14} strokeWidth={2.5} />
      <span>{text}</span>
    </span>
  );
}
