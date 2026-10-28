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
  closed = false;

  constructor(public url: string) {
    FakeEventSource.last = this;
  }

  emit(line: object) {
    act(() => this.onmessage?.(new MessageEvent('message', { data: JSON.stringify(line) })));
  }

  close() {
    this.closed = true;
  }
}

describe('LogViewer', () => {
  beforeEach(() => vi.stubGlobal('EventSource', FakeEventSource));
  afterEach(() => vi.unstubAllGlobals());

  it('streams lines and marks stderr', () => {
    renderWithProviders(<LogViewer path="/workloads/server-1/sonarr/logs" />);
    const source = FakeEventSource.last;
    act(() => source.onopen?.());

    source.emit({ timestamp: '2026-01-01T12:00:01Z', stream: 'stdout', text: 'started' });
    source.emit({ timestamp: '2026-01-01T12:00:02Z', stream: 'stderr', text: 'disk almost full' });

    expect(source.url).toBe('/api/v1/workloads/server-1/sonarr/logs');
    expect(screen.getByText('● Live')).toBeInTheDocument();
    expect(screen.getByText('started')).toBeInTheDocument();
    expect(screen.getByText('12:00:02')).toBeInTheDocument();
    expect(screen.getByText('disk almost full').parentElement?.className).toContain('stderr');
  });

  it('ignores new lines while paused', async () => {
    const user = userEvent.setup();
    renderWithProviders(<LogViewer path="/logs" />);
    const source = FakeEventSource.last;

    await user.click(screen.getByRole('button', { name: 'Pause' }));
    source.emit({ timestamp: null, stream: 'stdout', text: 'hidden' });

    expect(screen.queryByText('hidden')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resume' })).toBeInTheDocument();
  });

  it('closes the stream when it leaves the page', () => {
    const { unmount } = renderWithProviders(<LogViewer path="/logs" />);
    const source = FakeEventSource.last;

    unmount();

    expect(source.closed).toBe(true);
  });
});
