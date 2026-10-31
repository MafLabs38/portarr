import type { LucideIcon } from 'lucide-react';

import * as icons from 'Helpers/Props/icons';
import * as kinds from 'Helpers/Props/kinds';
import type { DeployedImage } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

import styles from './ImageStatusLabel.module.css';

function look(image: DeployedImage): { kind: kinds.Kind; icon: LucideIcon; text: string } {
  switch (image.status) {
    case 'behind':
      return {
        kind: image.change === 'major' ? kinds.WARNING : kinds.INFO,
        icon: icons.UPDATE,
        text: translate(`Change_${image.change ?? 'patch'}`),
      };
    case 'newer-build':
      return { kind: kinds.INFO, icon: icons.UPDATE, text: translate('ImageStatus_newer-build') };
    case 'up-to-date':
      return { kind: kinds.SUCCESS, icon: icons.HEALTHY, text: translate('ImageStatus_up-to-date') };
    default:
      return { kind: kinds.DISABLED, icon: icons.UNKNOWN, text: translate(`ImageStatus_${image.status}`) };
  }
}

/** Where a running image stands against its registry: icon plus text, never color alone. */
export default function ImageStatusLabel({ image }: Readonly<{ image: DeployedImage }>) {
  const { kind, icon: Icon, text } = look(image);
  return (
    <span className={`${styles.label} ${styles[kind]}`}>
      <Icon aria-hidden="true" size={14} strokeWidth={2.5} />
      <span>{text}</span>
    </span>
  );
}
