import { useEffect, useState } from 'react';

import usePermissions from 'Auth/usePermissions';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { CHAT_USE } from 'Helpers/Props/permissions';
import type ChatStatus from 'typings/Chat';
import type { ChatEvent, ConversationSummary } from 'typings/Chat';

const base = '/assistant/chat';
const conversationsPath = `${base}/conversations`;

/** Whether the chat is on (PORTARR__AGENT__URL), and its agents and models. */
export function useChatStatus() {
  // Only roles with chat.use ask: the others would get a 403 (V7).
  const { can } = usePermissions();
  return useApiQuery<ChatStatus>({ path: base, staleTime: 60_000, enabled: can(CHAT_USE) });
}

export function useConversations(enabled: boolean) {
  return useApiQuery<ConversationSummary[]>({ path: conversationsPath, enabled, refetchInterval: 30_000 });
}

export function useCreateConversation() {
  return useApiMutation<{ agent?: string; model?: string }, ConversationSummary>({
    path: conversationsPath,
    method: 'POST',
    invalidate: [conversationsPath],
  });
}

export function useDeleteConversation() {
  return useApiMutation<{ id: string }, undefined>({
    path: ({ id }) => `${conversationsPath}/${id}`,
    method: 'DELETE',
    invalidate: [conversationsPath],
  });
}

export function useSendMessage() {
  return useApiMutation<{ id: string; text: string; model?: string }, unknown>({
    path: ({ id }) => `${conversationsPath}/${id}/messages`,
    method: 'POST',
    invalidate: [conversationsPath],
  });
}

export function useAnswerPermission() {
  return useApiMutation<{ id: string; requestId: string; approve: boolean }, unknown>({
    path: ({ id, requestId }) => `${conversationsPath}/${id}/permissions/${encodeURIComponent(requestId)}`,
    method: 'POST',
    invalidate: [],
  });
}

export function useCancelTurn() {
  return useApiMutation<{ id: string }, unknown>({
    path: ({ id }) => `${conversationsPath}/${id}/cancel`,
    method: 'POST',
    invalidate: [],
  });
}

export interface ChatStream {
  /** Stored events, in order (history and new ones). */
  events: ChatEvent[];
  /** The answer being written, from the live text deltas. */
  draft: string;
  state: 'running' | 'idle' | 'stopped' | 'connecting';
  connected: boolean;
}

const initial: ChatStream = { events: [], draft: '', state: 'connecting', connected: false };

function apply(stream: ChatStream, event: ChatEvent): ChatStream {
  switch (event.type) {
    case 'text-delta':
      return { ...stream, draft: stream.draft + event.text };
    case 'status':
      return { ...stream, state: event.state, draft: event.state === 'running' ? stream.draft : '' };
    default: {
      // Replays after a reconnect may repeat events: the sequence number tells.
      if (event.seq <= (stream.events.at(-1)?.seq ?? 0)) {
        return stream;
      }
      const draft =
        event.type === 'text' || event.type === 'tool-call' || event.type === 'turn-end' ? '' : stream.draft;
      return { ...stream, events: [...stream.events, event], draft };
    }
  }
}

/**
 * A conversation's events (D-89): the server-sent stream replays the stored history, then the live events.
 * EventSource reconnects by itself and resumes after the last event it got (Last-Event-ID).
 */
export function useChatStream(conversationId: string | null): ChatStream {
  // Keyed by conversation: switching conversations shows an empty stream until the new one replays.
  const [state, setState] = useState<{ id: string | null; stream: ChatStream }>({ id: null, stream: initial });

  useEffect(() => {
    if (!conversationId || typeof EventSource === 'undefined') {
      return undefined;
    }
    const update = (change: (stream: ChatStream) => ChatStream) =>
      setState((current) => ({
        id: conversationId,
        stream: change(current.id === conversationId ? current.stream : initial),
      }));
    const source = new EventSource(`/api/v1${conversationsPath}/${conversationId}/events`);
    source.onopen = () => update((stream) => ({ ...stream, connected: true }));
    source.onerror = () => update((stream) => ({ ...stream, connected: false }));
    source.addEventListener('chat', (message: MessageEvent<string>) => {
      const event = JSON.parse(message.data) as ChatEvent;
      update((stream) => apply(stream, event));
    });
    return () => source.close();
  }, [conversationId]);

  return state.id === conversationId ? state.stream : initial;
}
