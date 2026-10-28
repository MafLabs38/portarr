import { useEffect, useRef, useState } from 'react';

import * as icons from 'Helpers/Props/icons';
import type { LogLine } from 'typings/Workload';
import translate from 'Utilities/String/translate';

import styles from './LogViewer.module.css';

const MAX_LINES = 1000;

interface LogViewerProps {
  /** Server-sent events endpoint, below /api/v1. */
  path: string;
}

/** Live logs (F-LOG-01): last lines then new ones, stderr highlighted, pause keeps the view still. */
export default function LogViewer({ path }: LogViewerProps) {
  const [lines, setLines] = useState<LogLine[]>([]);
  const [paused, setPaused] = useState(false);
  const [connected, setConnected] = useState(false);
  const pausedRef = useRef(paused);
  const linesRef = useRef<HTMLDivElement>(null);

  // The event handler reads the latest value without reconnecting the stream.
  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  useEffect(() => {
    const source = new EventSource(`/api/v1${path}`);
    source.onopen = () => setConnected(true);
    source.onerror = () => setConnected(false);
    source.onmessage = (event: MessageEvent<string>) => {
      if (pausedRef.current) {
        return;
      }
      const line = JSON.parse(event.data) as LogLine;
      setLines((current) => [...current.slice(-(MAX_LINES - 1)), line]);
    };

    return () => source.close();
  }, [path]);

  // Follow new lines by scrolling the log area only, never the page.
  useEffect(() => {
    const element = linesRef.current;
    if (!paused && element) {
      element.scrollTop = element.scrollHeight;
    }
  }, [lines, paused]);

  const PauseIcon = paused ? icons.PLAY : icons.PAUSE;

  return (
    <section aria-label={translate('Logs')} className={styles.viewer}>
      <header className={styles.toolbar}>
        <span className={connected ? styles.live : styles.offline}>
          {translate(connected ? 'LogsLive' : 'LogsDisconnected')}
        </span>
        <button className={styles.button} onClick={() => setPaused(!paused)} type="button">
          <PauseIcon aria-hidden="true" size={14} />
          {translate(paused ? 'Resume' : 'Pause')}
        </button>
      </header>
      <div className={styles.lines} ref={linesRef} role="log">
        {lines.length === 0 ? <p className={styles.placeholder}>{translate('LogsWaiting')}</p> : null}
        {lines.map((line, index) => (
          <div className={line.stream === 'stderr' ? styles.stderr : styles.line} key={index}>
            {line.timestamp ? <time className={styles.time}>{line.timestamp.slice(11, 19)}</time> : null}
            <span>{line.text}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
