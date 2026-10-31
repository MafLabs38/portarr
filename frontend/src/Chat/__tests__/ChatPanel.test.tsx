import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import chatItems from 'Chat/chatItems';
import ChatPanel from 'Chat/ChatPanel';
import { useChatStore } from 'Chat/chatStore';
import ChatToggle from 'Chat/ChatToggle';
import type ChatStatus from 'typings/Chat';
import type { ChatEvent } from 'typings/Chat';

const conversationId = '0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f';

const status: ChatStatus = {
  enabled: true,
  reachable: true,
  agents: [
    {
      id: 'claude-code',
      name: 'Claude Code',
      defaultModel: 'sonnet',
      available: true,
      models: [
        { id: 'sonnet', label: 'Sonnet' },
        { id: 'opus', label: 'Opus' },
      ],
    },
  ],
};

const history: ChatEvent[] = [
  { seq: 1, at: '2026-01-01T12:00:00Z', type: 'user', text: 'Add a note to the provider object-storage' },
  {
    seq: 2,
    at: '2026-01-01T12:00:01Z',
    type: 'tool-call',
    id: 't1',
    name: 'mcp__portarr__get_catalog_item',
    input: { section: 'providers' },
  },
  {
    seq: 3,
    at: '2026-01-01T12:00:02Z',
    type: 'tool-result',
    id: 't1',
    isError: false,
    summary: '{"version":"0123456789abcdef"}',
  },
  { seq: 4, at: '2026-01-01T12:00:03Z', type: 'text', text: 'I will set the **notes**.' },
  {
    seq: 5,
    at: '2026-01-01T12:00:04Z',
    type: 'permission',
    requestId: 'req-1',
    toolName: 'mcp__portarr__set_notes',
    input: { section: 'providers', id: 'object-storage', notes: 'Bucket example-backups.' },
  },
];

// jsdom has no EventSource: a controllable stand-in records the last opened stream.
class FakeEventSource {
  static last: FakeEventSource | undefined;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  listeners: Record<string, (event: MessageEvent<string>) => void> = {};
  url: string;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.last = this;
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners[type] = listener;
  }

  emit(events: ChatEvent[]) {
    act(() =>
      events.forEach((event) => this.listeners.chat?.(new MessageEvent('chat', { data: JSON.stringify(event) }))),
    );
  }

  close() {}
}

function useChat(open = true) {
  server.use(
    http.get('/api/v1/assistant/chat', () => HttpResponse.json(status)),
    http.get('/api/v1/assistant/chat/conversations', () =>
      HttpResponse.json([
        {
          id: conversationId,
          title: 'Add a note to the provider object-storage',
          agent: 'claude-code',
          model: 'sonnet',
          createdAt: '2026-01-01T12:00:00Z',
          updatedAt: '2026-01-01T12:00:04Z',
          running: true,
          busy: true,
          pendingPermissions: 1,
        },
      ]),
    ),
  );
  useChatStore.setState({ open, conversationId: open ? conversationId : null, agent: null, model: null });
}

describe('chat', () => {
  beforeEach(() => vi.stubGlobal('EventSource', FakeEventSource));
  afterEach(() => {
    vi.unstubAllGlobals();
    FakeEventSource.last = undefined;
  });

  it('has no header icon when the chat is off', async () => {
    renderWithProviders(<ChatToggle />);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByRole('button', { name: 'Chat' })).not.toBeInTheDocument();
  });

  it('opens the panel from the header icon', async () => {
    useChat(false);
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <ChatToggle />
        <ChatPanel />
      </>,
    );

    const toggle = await screen.findByRole('button', { name: 'Chat' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);
    expect(await screen.findByRole('complementary', { name: 'Chat' })).toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await user.click(screen.getByRole('button', { name: 'Close the chat' }));
    expect(screen.queryByRole('complementary', { name: 'Chat' })).not.toBeInTheDocument();
  });

  it('replays the history: messages, a collapsible tool call, and a permission card that approves', async () => {
    useChat();
    let answered: unknown;
    server.use(
      http.post('/api/v1/assistant/chat/conversations/:id/permissions/:requestId', async ({ request, params }) => {
        answered = { body: await request.json(), requestId: params.requestId };
        return HttpResponse.json({ approved: true });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ChatPanel />);

    await screen.findByRole('complementary', { name: 'Chat' });
    expect(FakeEventSource.last?.url).toBe(`/api/v1/assistant/chat/conversations/${conversationId}/events`);
    FakeEventSource.last?.emit(history);

    const log = screen.getByRole('log', { name: 'Conversation' });
    expect(within(log).getByText('Add a note to the provider object-storage')).toBeInTheDocument();
    expect(within(log).getByText('notes', { selector: 'strong' })).toBeInTheDocument();
    const tool = within(log).getByText('get_catalog_item').closest('details') as HTMLElement;
    expect(tool).not.toHaveAttribute('open');
    expect(within(tool).getByText('done')).toBeInTheDocument();

    const card = within(log).getByRole('region', { name: 'Apply this change?' });
    expect(within(card).getByText('set_notes')).toBeInTheDocument();
    expect(within(card).getByText(/"notes": "Bucket example-backups."/)).toBeInTheDocument();
    await user.click(within(card).getByRole('button', { name: 'Approve' }));
    await vi.waitFor(() =>
      expect(answered).toEqual({ body: { id: conversationId, requestId: 'req-1', approve: true }, requestId: 'req-1' }),
    );

    FakeEventSource.last?.emit([
      { seq: 6, at: '2026-01-01T12:00:05Z', type: 'permission-resolved', requestId: 'req-1', approved: true },
    ]);
    expect(within(card).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(within(card).getByText(/^Approved/)).toBeInTheDocument();
  });

  it('sends on Enter, keeps Shift+Enter for a new line and takes pasted text', async () => {
    useChat();
    let sent: unknown;
    server.use(
      http.post('/api/v1/assistant/chat/conversations/:id/messages', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ accepted: true }, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ChatPanel />);
    await screen.findByRole('complementary', { name: 'Chat' });
    FakeEventSource.last?.emit([{ seq: 1, at: '2026-01-01T12:00:00Z', type: 'status', state: 'idle' }]);

    const prompt = screen.getByRole('textbox', { name: 'Message' });
    await user.click(prompt);
    await user.paste('Note that the blog uses the CDN');
    await user.keyboard('{Shift>}{Enter}{/Shift}second line');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Model' }), 'opus');
    await user.click(prompt);
    await user.keyboard('{Enter}');

    await vi.waitFor(() =>
      expect(sent).toEqual({ id: conversationId, text: 'Note that the blog uses the CDN\nsecond line', model: 'opus' }),
    );
    expect(prompt).toHaveValue('');
  });

  it('shows Stop while a turn runs and the usage of a finished turn', async () => {
    useChat();
    let cancelled = false;
    server.use(
      http.post('/api/v1/assistant/chat/conversations/:id/cancel', () => {
        cancelled = true;
        return HttpResponse.json({ accepted: true }, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ChatPanel />);
    await screen.findByRole('complementary', { name: 'Chat' });
    FakeEventSource.last?.emit([
      { seq: 0, at: '2026-01-01T12:00:00Z', type: 'status', state: 'running' },
      { seq: 0, at: '2026-01-01T12:00:00Z', type: 'text-delta', text: 'Three machines' },
    ]);
    expect(screen.getByText('Three machines')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop' }));
    await vi.waitFor(() => expect(cancelled).toBe(true));

    FakeEventSource.last?.emit([
      {
        seq: 1,
        at: '2026-01-01T12:00:01Z',
        type: 'turn-end',
        isError: false,
        usage: {
          inputTokens: 10,
          outputTokens: 375,
          cacheReadTokens: 1000,
          cacheWriteTokens: 0,
          costUsd: 0.0168,
          durationMs: 5348,
        },
      },
      { seq: 1, at: '2026-01-01T12:00:01Z', type: 'status', state: 'idle' },
    ]);
    expect(screen.getByText('1,010 tokens in, 375 out · $0.0168 · 5.3 s')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument();
  });
});

describe('chatItems', () => {
  it('joins a result to its call and an answer to its card', () => {
    const items = chatItems([
      ...history,
      {
        seq: 6,
        at: '2026-01-01T12:00:05Z',
        type: 'permission-resolved',
        requestId: 'req-1',
        approved: false,
        reason: 'cancelled',
      },
    ]);
    expect(items.map((item) => item.kind)).toEqual(['user', 'tool', 'text', 'permission']);
    expect(items[1]).toMatchObject({ result: { isError: false } });
    expect(items[3]).toMatchObject({ state: 'cancelled' });
  });
});
