import { type CSSProperties, type KeyboardEvent, type PointerEvent, useMemo, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import * as icons from 'Helpers/Props/icons';
import type ChatStatus from 'typings/Chat';
import translate from 'Utilities/String/translate';

import chatItems from './chatItems';
import ChatMessages from './ChatMessages';
import ChatNotices, { type ChatNotice } from './ChatNotices';
import styles from './ChatPanel.module.css';
import { maxPanelWidth, minPanelWidth, useChatStore } from './chatStore';
import ConversationList from './ConversationList';
import PromptBox from './PromptBox';
import {
  useAnswerPermission,
  useCancelTurn,
  useChatStatus,
  useChatStream,
  useConversations,
  useCreateConversation,
  useDeleteConversation,
  useSendMessage,
} from './useChat';

const keyboardStep = 32;

/** The agent's MCP tools change the catalog only when the MCP server, its write tools and a writable catalog are all on. */
const chatCanWrite = (status: ChatStatus) =>
  status.mcpEnabled === true && status.mcpWriteTools === true && status.catalogWritable === true;

/** What the operator may want to know about the chat, in the order of how much it matters. */
function noticesOf(status: ChatStatus, current: { name: string; available: boolean } | undefined): ChatNotice[] {
  const notices: ChatNotice[] = [];
  if (!status.reachable) {
    notices.push({ id: 'unreachable', kind: 'warning', text: translate('ChatUnreachable') });
  }
  if (current && !current.available) {
    notices.push({ id: 'credentials', kind: 'warning', text: translate('ChatNoCredentials', { agent: current.name }) });
  }
  if (status.reachable && status.mcpEnabled !== undefined && !chatCanWrite(status)) {
    notices.push({ id: 'read-only', kind: 'info', text: translate('ChatReadOnlyHint') });
  }
  return notices;
}

/**
 * The built-in chat (D-90), like an IDE's chat panel: a resizable side panel with the conversation history, the
 * messages with their tool calls and permission cards, and the prompt. Behind it, the portarr-agent runner whose
 * only tool is Portarr's MCP server (D-87, D-88). Full screen on phones. What the operator may want to know about the
 * chat (the runner does not answer, no credentials, a read-only catalog) is behind a bell of the header, never an alert
 * in the middle of the conversation.
 */
export default function ChatPanel() {
  const status = useChatStatus();
  const { open, width, conversationId, agent, model, setOpen, setWidth, setConversation, setAgent, setModel } =
    useChatStore();
  const enabled = Boolean(status.data?.enabled) && open;
  const conversations = useConversations(enabled);
  const stream = useChatStream(enabled ? conversationId : null);
  const create = useCreateConversation();
  const remove = useDeleteConversation();
  const send = useSendMessage();
  const answer = useAnswerPermission();
  const cancel = useCancelTurn();
  const [historyOpen, setHistoryOpen] = useState(false);
  const items = useMemo(() => chatItems(stream.events), [stream.events]);

  if (!enabled || !status.data) {
    return null;
  }

  const agents = status.data.agents ?? [];
  const current = agents.find((option) => option.id === agent) ?? agents[0];
  const currentModel = current?.models.some((option) => option.id === model)
    ? (model as string)
    : current?.defaultModel;
  const conversation = conversations.data?.find((summary) => summary.id === conversationId);
  const running = stream.state === 'running' || send.isPending || create.isPending;
  const failure = send.error ?? create.error ?? answer.error ?? cancel.error;
  const notices = noticesOf(status.data, current);

  const onSend = async (text: string) => {
    if (!current || !currentModel) {
      return;
    }
    let id = conversationId;
    if (!id) {
      const created = await create.mutateAsync({ agent: current.id, model: currentModel });
      id = created.id;
      setConversation(id);
    }
    send.mutate({ id, text, model: currentModel });
  };

  const startResize = (event: PointerEvent<HTMLDivElement>) => {
    const startX = event.clientX;
    const startWidth = width;
    const target = event.currentTarget;
    target.setPointerCapture(event.pointerId);
    const move = (moveEvent: globalThis.PointerEvent) => setWidth(startWidth + startX - moveEvent.clientX);
    const stop = () => {
      target.removeEventListener('pointermove', move);
      target.removeEventListener('pointerup', stop);
    };
    target.addEventListener('pointermove', move);
    target.addEventListener('pointerup', stop);
  };

  const resizeWithKeys = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault();
      setWidth(width + (event.key === 'ArrowLeft' ? keyboardStep : -keyboardStep));
    }
  };

  return (
    <aside
      aria-label={translate('Chat')}
      className={styles.panel}
      id="chat-panel"
      style={{ '--chatPanelWidth': `${width}px` } as CSSProperties}
    >
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- a focusable separator is the WAI-ARIA window splitter: it takes the keyboard and the pointer. */}
      <div // NOSONAR
        aria-label={translate('ResizeChat')}
        aria-orientation="vertical"
        aria-valuemax={maxPanelWidth}
        aria-valuemin={minPanelWidth}
        aria-valuenow={width}
        className={styles.resizer}
        onKeyDown={resizeWithKeys}
        onPointerDown={startResize}
        role="separator"
        tabIndex={0} // NOSONAR
      />
      <header className={styles.bar}>
        <h2 className={styles.title}>{conversation?.title || translate('NewChat')}</h2>
        <ChatNotices notices={notices} />
        <button
          aria-expanded={historyOpen}
          aria-label={translate('ChatHistory')}
          className={historyOpen ? `${styles.iconButton} ${styles.pressed}` : styles.iconButton}
          onClick={() => setHistoryOpen(!historyOpen)}
          title={translate('ChatHistory')}
          type="button"
        >
          <icons.CHAT_HISTORY aria-hidden="true" size={16} strokeWidth={2.25} />
        </button>
        <button
          aria-label={translate('NewChat')}
          className={styles.iconButton}
          disabled={running}
          onClick={() => {
            setConversation(null);
            setHistoryOpen(false);
          }}
          title={translate('NewChat')}
          type="button"
        >
          <icons.NEW_CHAT aria-hidden="true" size={16} strokeWidth={2.25} />
        </button>
        <button
          aria-label={translate('CloseChat')}
          className={styles.iconButton}
          onClick={() => setOpen(false)}
          title={translate('CloseChat')}
          type="button"
        >
          <icons.CLOSE aria-hidden="true" size={16} strokeWidth={2.25} />
        </button>
      </header>

      {historyOpen ? (
        <ConversationList
          conversations={conversations.data ?? []}
          currentId={conversationId}
          onDelete={(id) => {
            remove.mutate({ id });
            if (id === conversationId) {
              setConversation(null);
            }
          }}
          onOpen={(id) => {
            const opened = conversations.data?.find((summary) => summary.id === id);
            if (opened) {
              // The selectors follow the conversation: its next turn keeps its model unless changed here.
              setAgent(opened.agent, opened.model);
            }
            setConversation(id);
            setHistoryOpen(false);
          }}
        />
      ) : null}

      <p className={styles.scope}>
        {translate(
          status.data.mcpEnabled === undefined || chatCanWrite(status.data) ? 'ChatScope' : 'ChatScopeReadOnly',
        )}
      </p>
      {failure ? <Alert kind="danger">{translate('ChatRequestFailed', { status: failure.status })}</Alert> : null}

      <ChatMessages
        answering={answer.isPending}
        draft={stream.draft}
        items={items}
        onAnswer={(requestId, approve) => conversationId && answer.mutate({ id: conversationId, requestId, approve })}
        running={running}
      />
      <PromptBox
        agent={current?.id ?? ''}
        agents={agents}
        disabled={!current?.available}
        model={currentModel ?? ''}
        onAgentChange={(id) => setAgent(id, agents.find((option) => option.id === id)?.defaultModel ?? '')}
        onModelChange={setModel}
        onSend={(text) => void onSend(text)}
        onStop={() => conversationId && cancel.mutate({ id: conversationId })}
        running={running}
      />
    </aside>
  );
}
