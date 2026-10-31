import { useEffect, useState } from 'react';

import translate from 'Utilities/String/translate';

import styles from './LiveIndicator.module.css';

interface LiveIndicatorProps {
  /** Time of the newest measurement, or null before the first one. */
  at: string | null;
  /** How often a new measurement is expected, in seconds. */
  expectedSeconds: number;
}

/** A sample older than this many expected intervals (and at least 15 s) is shown as stale. */
const staleFactor = 3;
const staleFloorSeconds = 15;

/** "● 2 s ago": the age of the last sample, ticking every second, so the operator sees the page is live. */
export default function LiveIndicator({ at, expectedSeconds }: Readonly<LiveIndicatorProps>) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => window.clearInterval(timer);
  }, []);

  if (at === null) {
    return null;
  }

  const age = Math.max(0, Math.round((now - Date.parse(at)) / 1_000));
  const stale = age > Math.max(expectedSeconds * staleFactor, staleFloorSeconds);
  const ageText =
    age < 60 ? translate('SecondsAgo', { count: age }) : translate('MinutesAgo', { count: Math.floor(age / 60) });

  return (
    <span
      className={stale ? styles.stale : styles.live}
      title={translate('LastSampleAt', { time: new Date(at).toLocaleTimeString() })}
    >
      {translate(stale ? 'StaleAge' : 'LiveAge', { age: ageText })}
    </span>
  );
}
