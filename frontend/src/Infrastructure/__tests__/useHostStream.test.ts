import { act, renderHook } from '@testing-library/react';
import useHostStream, { hostKey } from 'Infrastructure/useHostStream';

import type { HostMetrics } from 'typings/Infrastructure';

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  private readonly listeners = new Map<string, (event: MessageEvent<string>) => void>();

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, listener);
  }

  emit(type: string, data: unknown) {
    this.emitRaw(type, JSON.stringify(data));
  }

  emitRaw(type: string, data: string) {
    this.listeners.get(type)?.({ data } as MessageEvent<string>);
  }

  close() {
    this.closed = true;
  }
}

const read = (machineId: string, cpuPercent: number) =>
  ({ environmentId: 'server-1', machineId, cpuPercent }) as unknown as HostMetrics;

describe('useHostStream', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('opens the stream, is live while it is open and closes it on unmount', () => {
    const { result, unmount } = renderHook(() => useHostStream());
    const [source] = FakeEventSource.instances;
    expect(source?.url).toBe('/api/v1/infrastructure/stream');
    expect(result.current.live).toBe(false);

    act(() => source?.onopen?.());
    expect(result.current.live).toBe(true);

    act(() => source?.onerror?.());
    expect(result.current.live).toBe(false);

    unmount();
    expect(source?.closed).toBe(true);
  });

  it('is live again when the browser reconnects, and keeps applying the reads', () => {
    const { result } = renderHook(() => useHostStream());
    const [source] = FakeEventSource.instances;

    act(() => source?.onopen?.());
    act(() => source?.onerror?.());
    expect(result.current.live).toBe(false);

    act(() => source?.onopen?.());
    act(() => {
      source?.emit('host', read('a', 10));
      vi.advanceTimersByTime(250);
    });

    expect(result.current.live).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(result.current.reads.get(hostKey('server-1', 'a'))).toHaveLength(1);
  });

  it('ignores a malformed event and keeps the reads that follow', () => {
    const { result } = renderHook(() => useHostStream());
    const [source] = FakeEventSource.instances;

    act(() => {
      source?.emitRaw('host', '{ not json');
      source?.emit('host', read('a', 10));
      vi.advanceTimersByTime(250);
    });

    expect(result.current.reads.get(hostKey('server-1', 'a'))?.map((r) => r.cpuPercent)).toEqual([10]);
  });

  it('applies the reads in batches, per machine, oldest first', () => {
    const { result } = renderHook(() => useHostStream());
    const [source] = FakeEventSource.instances;

    act(() => {
      source?.emit('host', read('a', 10));
      source?.emit('host', read('b', 20));
      source?.emit('host', read('a', 30));
    });
    expect(result.current.reads.size).toBe(0);

    act(() => {
      vi.advanceTimersByTime(250);
    });

    expect(result.current.live).toBe(true);
    expect(result.current.reads.get(hostKey('server-1', 'a'))?.map((r) => r.cpuPercent)).toEqual([10, 30]);
    expect(result.current.reads.get(hostKey('server-1', 'b'))?.map((r) => r.cpuPercent)).toEqual([20]);
  });

  it('keeps the last 120 reads of a machine', () => {
    const { result } = renderHook(() => useHostStream());
    const [source] = FakeEventSource.instances;

    act(() => {
      for (let i = 0; i < 130; i++) {
        source?.emit('host', read('a', i));
      }
      vi.advanceTimersByTime(250);
    });

    const kept = result.current.reads.get(hostKey('server-1', 'a')) ?? [];
    expect(kept).toHaveLength(120);
    expect(kept[0]?.cpuPercent).toBe(10);
  });

  it('does nothing in a browser without server-sent events', () => {
    vi.stubGlobal('EventSource', undefined);

    const { result } = renderHook(() => useHostStream());

    expect(result.current).toEqual({ live: false, reads: new Map() });
  });
});
