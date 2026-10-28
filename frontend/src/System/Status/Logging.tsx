import type { ChangeEvent } from 'react';

import Card from 'Components/Card';
import type { LogLevel } from 'typings/Logging';
import translate from 'Utilities/String/translate';

import styles from './Logging.module.css';
import { useLogging, useUpdateLogging } from './useLogging';

const levels: LogLevel[] = ['Trace', 'Debug', 'Information', 'Warning', 'Error'];

export default function Logging() {
  const { data } = useLogging();
  const { mutate, isPending, isError } = useUpdateLogging();

  const onChange = (event: ChangeEvent<HTMLSelectElement>) => {
    mutate({ level: event.target.value as LogLevel });
  };

  return (
    <Card title={translate('Logging')}>
      <label className={styles.row}>
        <span className={styles.label}>{translate('LogLevel')}</span>
        <select className={styles.select} disabled={!data || isPending} onChange={onChange} value={data?.level ?? ''}>
          {levels.map((level) => (
            <option key={level} value={level}>
              {translate(`LogLevel${level}`)}
            </option>
          ))}
        </select>
      </label>
      <p className={styles.help}>{translate('LogLevelHelpText')}</p>
      {isError ? <p role="alert">{translate('ErrorSavingLogLevel')}</p> : null}
    </Card>
  );
}
