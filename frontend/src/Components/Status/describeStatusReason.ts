import type Status from 'typings/Status';
import translate from 'Utilities/String/translate';

/**
 * Why a status is what it is. The server gives a code with its arguments (`timeout`, `certificate-expires-in-days` with the days);
 * a reason that is not a known code is already a sentence and is shown as it is.
 */
export default function describeStatusReason(status: Status): string {
  const key = `StatusReason_${status.reason}`;
  const text = translate(key);
  if (text === key) {
    return status.reason;
  }

  const [first = '', second = ''] = status.reasonArgs ?? [];
  return translate(key, { code: first, days: first, ms: second });
}
