import type { DataAsset, DataCopy, DataSourceCheck } from 'typings/Data';
import formatAge, { formatUntil, formatWindow } from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

/** A reason code in words, or the code itself when no text exists for it yet. */
export function describeReason(code: string): string {
  const key = `Reason_${code}`;
  const text = translate(key);
  return text === key ? code : text;
}

/**
 * "last success 3 h ago", "has not run yet · next run in 3 h", "never succeeded": what is known of a copy. A job
 * that never ran is waiting (with its next run when the schedule tells); a tool that is not set up cannot say.
 */
export function describeLastSuccess(copy: DataCopy, now: number = Date.now()): string {
  if (copy.state === 'waiting') {
    return copy.nextRunAt
      ? translate('WaitingNextRun', { time: formatUntil(copy.nextRunAt, now) })
      : translate('WaitingFirstRun');
  }
  if (copy.state === 'not-set-up') {
    return translate('SetUpTheTool');
  }
  return copy.lastSuccessAt
    ? translate('LastSuccessAge', { age: formatAge(copy.lastSuccessAt, now) })
    : translate('NeverSucceeded');
}

/** The accepted loss window: "within 24 h", "within 7 days". */
export function describeWindow(asset: DataAsset): string {
  return translate('WithinWindow', { window: formatWindow(asset.maxAgeSeconds) });
}

/** "Backrest · plan db-local", "CronJob · shop/dump": the job behind a copy. */
export function describeSource(source: DataSourceCheck): string {
  const name = source.jobName ?? source.reference;
  const tool = translate(`Tool_${source.tool}`);
  return `${tool} · ${name}`;
}
