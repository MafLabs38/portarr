import translate from 'Utilities/String/translate';

/** A reason code of the image scanner in words; a code nobody listed (`http-502`) is shown as it is. */
export default function scanReason(reason: string | null): string {
  if (reason === null) {
    return translate('StatusUnknown');
  }
  const key = `ScanReason_${reason}`;
  const text = translate(key);
  return text === key ? reason : text;
}
