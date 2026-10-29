import { useEffect, useRef, useState } from 'react';

import * as icons from 'Helpers/Props/icons';
import type { LogLine } from 'typings/Workload';
import translate from 'Utilities/String/translate';

import styles from './LogViewer.module.css';

const MAX_LINES = 1000;

type Connection = 'connecting' | 'live' | 'ended' | 'disconnected';

interface Row extends LogLine {
  id: number;
}

interface LogViewerProps {
  /** Server-sent events endpoint, below /api/v1. */
  path: string;
}

function parse(data: string): LogLine | null {
  try {
    return JSON.parse(data) as LogLine;
  } catch {
    return null;
  }
}

/**
 * Live logs (F-LOG-01): last lines then new ones, stderr marked. Pause buffers new lines and
 * flushes them on resume. The server sends `end` when the container stops writing (it exited):
 * the stream is closed instead of reconnecting and replaying the tail.
 */
export default function LogViewer({ path }: Readonly<LogViewerProps>) {
  const [rows, setRows] = useState<Row[]>([]);
  const [paused, setPaused] = useState(false);
  const [connection, setConnection] = useState<Connection>('connecting');
  const pausedRef = useRef(paused);
  const bufferRef = useRef<Row[]>([]);
  const nextId = useRef(0);
  const linesRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    pausedRef.current = paused;
    if (!paused && bufferRef.current.length > 0) {
      const buffered = bufferRef.current;
      bufferRef.current = [];
      setRows((current) => [...current, ...buffered].slice(-MAX_LINES));
    }
  }, [paused]);

  useEffect(() => {
    const source = new EventSource(`/api/v1${path}`);

    // A reconnect replays the tail: start from a clean view.
    source.onopen = () => {
      setRows([]);
      bufferRef.current = [];
      setConnection('live');
    };
    source.onerror = () => setConnection((current) => (current === 'ended' ? current : 'disconnected'));
    source.onmessage = (event: MessageEvent<string>) => {
      const line = parse(event.data);
      if (!line) {
        return;
      }
      const row = { ...line, id: nextId.current++ };
      if (pausedRef.current) {
        bufferRef.current = [...bufferRef.current, row].slice(-MAX_LINES);
        return;
      }
      setRows((current) => [...current.slice(-(MAX_LINES - 1)), row]);
    };
    source.addEventListener('end', () => {
      setConnection('ended');
      source.close();
    });

    return () => source.close();
  }, [path]);

  // Follow new lines by scrolling the log area only, never the page.
  useEffect(() => {
    const element = linesRef.current;
    if (!paused && element) {
      element.scrollTop = element.scrollHeight;
    }
  }, [rows, paused]);

  const PauseIcon = paused ? icons.PLAY : icons.PAUSE;
  const status: Record<Connection, string> = {
    connecting: 'LogsConnecting',
    live: 'LogsLive',
    ended: 'LogsEnded',
    disconnected: 'LogsDisconnected',
  };

  return (
    <section aria-label={translate('Logs')} className={styles.viewer}>
      <header className={styles.toolbar}>
        <span className={connection === 'live' ? styles.live : styles.offline}>{translate(status[connection])}</span>
        <button className={styles.button} onClick={() => setPaused(!paused)} type="button">
          <PauseIcon aria-hidden="true" size={14} />
          {translate(paused ? 'Resume' : 'Pause')}
        </button>
      </header>
      {/* Focusable so keyboard users can scroll the log (axe scrollable-region-focusable): S6845 does not apply. */}
      <div className={styles.lines} ref={linesRef} role="log" tabIndex={0} /* NOSONAR */>
        {rows.length === 0 ? <p className={styles.placeholder}>{translate('LogsWaiting')}</p> : null}
        {rows.map((row) => (
          <div className={row.stream === 'stderr' ? styles.stderr : styles.line} data-stream={row.stream} key={row.id}>
            {row.timestamp ? <time className={styles.time}>{row.timestamp.slice(11, 19)}</time> : null}
            <span>
              {row.text}
              {row.truncated ? <span className={styles.truncated}> {translate('LogLineTruncated')}</span> : null}
            </span>
          </div>
        ))}
      </div>
    </section>
  );
}
