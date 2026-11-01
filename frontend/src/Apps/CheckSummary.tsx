import describeCheck from 'Apps/describeCheck';
import * as icons from 'Helpers/Props/icons';
import type { AppCheck } from 'typings/Check';
import translate from 'Utilities/String/translate';

import SummaryLine from './SummaryLine';

interface CheckSummaryProps {
  check: AppCheck | null;
  /** Reference time for the certificate expiry (when the data was fetched). */
  now: number;
}

/** The HTTP check line of a tile (V2): "200 · 140 ms", "HTTP 502", "Timeout", or "Not monitored". */
export default function CheckSummary({ check, now }: Readonly<CheckSummaryProps>) {
  const label = translate('HttpCheck');

  if (!check) {
    return <SummaryLine icon={icons.HTTP_CHECK} label={label} tone="muted" value={translate('NotMonitored')} />;
  }

  const { text, tone } = describeCheck(check.latest, now, check.status);
  // A non-critical check only degrades its app: warning color, not danger.
  const shownTone = tone === 'bad' && !check.critical ? 'warn' : tone;

  return (
    <SummaryLine icon={icons.HTTP_CHECK} label={label} title={`${label}: ${check.url}`} tone={shownTone} value={text} />
  );
}
