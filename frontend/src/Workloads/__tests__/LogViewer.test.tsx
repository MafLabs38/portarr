import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import renderWithProviders from '__mocks__/renderWithProviders';
import LogViewer from 'Workloads/Details/LogViewer';

// jsdom has no EventSource: a controllable stand-in records the last opened stream.
class FakeEventSource {
  static last: FakeEventSource;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: MessageEvent<string>) => void) | null = null;
  listeners: Record<string, () => void> = {};
  closed = false;

  constructor(public url: string) {
    FakeEventSource.last = this;
  }

  addEventListener(type: string, listener: () => void) {
    this.listeners[type] = listener;
  }

  open() {
    act(() => this.onopen?.());
  }

  emit(text: string, stream = 'stdout') {
    act(() => this.send(text, stream));
  }

  emitMany(texts: string[]) {
    act(() => texts.forEach((text) => this.send(text, 'stdout')));
  }

  private send(text: string, stream: string) {
    const data = JSON.stringify({ timestamp: '2026-01-01T12:00:02Z', stream, text, truncated: false });
    this.onmessage?.(new MessageEvent('message', { data }));
  }

  close() {
    this.closed = true;
  }
}

function render() {
  const view = renderWithProviders(<LogViewer path="/workloads/server-1/sonarr/logs" />);
  const source = FakeEventSource.last;
  source.open();
  return { ...view, source };
}

describe('LogViewer', () => {
  beforeEach(() => vi.stubGlobal('EventSource', FakeEventSource));
  afterEach(() => vi.unstubAllGlobals());

  it('streams lines with their time and stream', () => {
    const { source } = render();

    source.emit('started');
    source.emit('disk almost full', 'stderr');

    expect(source.url).toBe('/api/v1/workloads/server-1/sonarr/logs');
    expect(screen.getByText('● Live')).toBeInTheDocument();
    expect(screen.getAllByText('12:00:02')).toHaveLength(2);
    expect(screen.getByText('disk almost full').closest('[data-stream]')).toHaveAttribute('data-stream', 'stderr');
  });

  it('buffers lines while paused and shows them on resume', async () => {
    const user = userEvent.setup();
    const { source } = render();

    await user.click(screen.getByRole('button', { name: 'Pause' }));
    source.emit('while paused');
    expect(screen.queryByText('while paused')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Resume' }));
    expect(screen.getByText('while paused')).toBeInTheDocument();
  });

  it('keeps the last 1000 lines', () => {
    const { source } = render();

    source.emitMany(Array.from({ length: 1001 }, (_, i) => `line ${i}`));

    expect(screen.queryByText('line 0')).not.toBeInTheDocument();
    expect(screen.getByText('line 1000')).toBeInTheDocument();
  });

  it('closes the stream when the container stops writing instead of reconnecting', () => {
    const { source } = render();

    act(() => source.listeners.end?.());

    expect(source.closed).toBe(true);
    expect(screen.getByText('■ Stopped writing (container not running)')).toBeInTheDocument();
  });

  it('says when the stream is lost, and starts clean when it reconnects', () => {
    const { source } = render();
    source.emit('before the drop');

    act(() => source.onerror?.());
    expect(screen.getByText('Disconnected, reconnecting…')).toBeInTheDocument();

    source.open();
    expect(screen.queryByText('before the drop')).not.toBeInTheDocument();
  });
});
