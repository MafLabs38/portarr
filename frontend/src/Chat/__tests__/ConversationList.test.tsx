import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import ConversationList from 'Chat/ConversationList';
import type { ConversationSummary } from 'typings/Chat';

const conversation = (over: Partial<ConversationSummary>): ConversationSummary => ({
  id: 'a',
  title: 'Add a note',
  agent: 'claude-code',
  model: 'sonnet',
  createdAt: '2026-01-01T12:00:00Z',
  updatedAt: new Date().toISOString(),
  running: false,
  busy: false,
  pendingPermissions: 0,
  ...over,
});

describe('ConversationList', () => {
  it('says there is nothing yet', () => {
    render(<ConversationList conversations={[]} currentId={null} onDelete={vi.fn()} onOpen={vi.fn()} />);

    expect(screen.getByText('No conversation yet.')).toBeInTheDocument();
  });

  it('opens and deletes a conversation, and marks the current one', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    const onDelete = vi.fn();
    render(
      <ConversationList
        conversations={[conversation({}), conversation({ id: 'b', title: '' })]}
        currentId="b"
        onDelete={onDelete}
        onOpen={onOpen}
      />,
    );

    const list = screen.getByRole('list', { name: 'Conversations' });
    await user.click(within(list).getByRole('button', { name: /^Add a note/ }));
    await user.click(screen.getByRole('button', { name: 'Delete the conversation New chat' }));

    expect(onOpen).toHaveBeenCalledWith('a');
    expect(onDelete).toHaveBeenCalledWith('b');
    expect(within(list).getByRole('button', { name: /^New chat/ })).toHaveAttribute('aria-current', 'true');
    expect(within(list).getByRole('button', { name: /^Add a note/ })).not.toHaveAttribute('aria-current');
  });

  it('says a conversation waits for the operator, and otherwise which model and how long ago', () => {
    render(
      <ConversationList
        conversations={[conversation({ pendingPermissions: 1 }), conversation({ id: 'b', title: 'Other' })]}
        currentId={null}
        onDelete={vi.fn()}
        onOpen={vi.fn()}
      />,
    );

    expect(screen.getByText('waiting for your approval')).toBeInTheDocument();
    expect(screen.getByText('sonnet · just now')).toBeInTheDocument();
  });
});
