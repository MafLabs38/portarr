import type { ImageScanner } from 'typings/Maintenance';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './ImageScannerStatus.module.css';
import scanReason from './scanReason';

/**
 * One line about the image scanner companion (D-131): when Portarr last read it, how old its vulnerability database is and,
 * when the last read failed, why (the previous results stay). Nothing while no scanner is declared: it is optional, not a nag.
 */
export default function ImageScannerStatus({ scanner }: Readonly<{ scanner: ImageScanner }>) {
  if (!scanner.configured) {
    return null;
  }

  return (
    <p className={styles.line}>
      <span>
        {translate('ImageScannerStatus')}:{' '}
        {scanner.readAt
          ? translate('ImageScannerRead', { age: formatAge(scanner.readAt) })
          : translate('ImageScannerNotReadYet')}
      </span>
      {scanner.databaseUpdatedAt ? (
        <span>{translate('ImageScannerDatabase', { age: formatAge(scanner.databaseUpdatedAt) })}</span>
      ) : null}
      {scanner.error ? (
        <span className={styles.error}>
          {translate('ImageScannerUnreadable', { reason: scanReason(scanner.error) })}
        </span>
      ) : null}
    </p>
  );
}
