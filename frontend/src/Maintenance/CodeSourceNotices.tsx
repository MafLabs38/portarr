import Alert from 'Components/Alert/Alert';
import type { CodeSource } from 'typings/Maintenance';
import translate from 'Utilities/String/translate';

/** Why the code side of the page may be empty: no source in the catalog, or the last scan failed. */
export default function CodeSourceNotices({ source }: Readonly<{ source: CodeSource }>) {
  return (
    <>
      {source.configured ? null : <Alert kind="info">{translate('CodeSourceMissingHint')}</Alert>}
      {source.error ? (
        <Alert kind="warning">{translate('CodeSourceError', { reason: translate(`Reason_${source.error}`) })}</Alert>
      ) : null}
    </>
  );
}
