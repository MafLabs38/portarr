import { useEffect, useRef, useState } from 'react';

import type { HostMetrics } from 'typings/Infrastructure';

/** Reads kept per machine between two snapshots: the snapshot then carries them in its history. */
const keptPerMachine = 120;

/** Reads are applied in batches, so five machines reading every second cost a few renders, not five. */
const flushDelayMs = 200;

export function hostKey(environmentId: string, machineId: string): string {
  return `${environmentId}/${machineId}`;
}

export interface HostStream {
  /** True while the stream is open; the page then polls the snapshot less often. */
  live: boolean;
  /** Recent reads per machine (hostKey), oldest first. */
  reads: Map<string, HostMetrics[]>;
}

/**
 * The live host agent reads (D-72): server-sent events from /infrastructure/stream, one per machine and second.
 * EventSource reconnects by itself; while it is down, the snapshot polling carries the points.
 */
export default function useHostStream(): HostStream {
  const [stream, setStream] = useState<HostStream>({ live: false, reads: new Map() });
  const pending = useRef<Map<string, HostMetrics[]>>(new Map());

  useEffect(() => {
    if (typeof EventSource === 'undefined') {
      return undefined;
    }

    let timer: ReturnType<typeof globalThis.setTimeout> | undefined;
    const flush = () => {
      timer = undefined;
      const batch = pending.current;
      pending.current = new Map();
      setStream((current) => {
        const reads = new Map(current.reads);
        for (const [key, added] of batch) {
          reads.set(key, [...(reads.get(key) ?? []), ...added].slice(-keptPerMachine));
        }
        return { live: true, reads };
      });
    };

    const source = new EventSource('/api/v1/infrastructure/stream');
    source.onopen = () => setStream((current) => (current.live ? current : { ...current, live: true }));
    source.onerror = () => setStream((current) => (current.live ? { ...current, live: false } : current));
    source.addEventListener('host', (event: MessageEvent<string>) => {
      let read: HostMetrics;
      try {
        read = JSON.parse(event.data) as HostMetrics;
      } catch {
        // One unreadable event is skipped: the next second brings the next read.
        return;
      }

      const key = hostKey(read.environmentId, read.machineId);
      pending.current.set(key, [...(pending.current.get(key) ?? []), read]);
      timer ??= globalThis.setTimeout(flush, flushDelayMs);
    });

    return () => {
      source.close();
      globalThis.clearTimeout(timer);
    };
  }, []);

  return stream;
}
