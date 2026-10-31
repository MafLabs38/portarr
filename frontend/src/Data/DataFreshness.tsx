import useNow from 'Helpers/Hooks/useNow';
import type { DataSource } from 'typings/Data';
import formatAge from 'Utilities/Date/formatAge';
import translate from 'Utilities/String/translate';

import styles from './DataFreshness.module.css';
import { describeReason } from './describeCopy';

interface DataFreshnessProps {
  sources: DataSource[];
}

/**
 * The backup tools the followed data expects and how fresh Portarr's reading of each is ("Backrest on server-1:
 * 12 plans, read 20 s ago"); a tool that cannot be read says why, and the copies it should prove stay unknown.
 */
export default function DataFreshness({ sources }: Readonly<DataFreshnessProps>) {
  const now = useNow(1_000);

  if (sources.length === 0) {
    return null;
  }

  return (
    <ul aria-label={translate('BackupToolsRead')} className={styles.bar}>
      {sources.map((source) => (
        <li className={source.error ? styles.problem : styles.source} key={`${source.tool}@${source.environmentId}`}>
          <strong>{translate(`Tool_${source.tool}`)}</strong> · {source.environmentName}
          {': '}
          {source.error
            ? describeReason(source.error)
            : `${translate('JobsCount', { count: source.jobs })}, ${translate('ReadAge', { age: formatAge(source.checkedAt, now) })}`}
        </li>
      ))}
    </ul>
  );
}
