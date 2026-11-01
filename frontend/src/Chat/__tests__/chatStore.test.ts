import { clampWidth, defaultPanelWidth, maxPanelWidth, minPanelWidth, useChatStore } from 'Chat/chatStore';
import formatArguments from 'Chat/formatArguments';

describe('chatStore', () => {
  it.each([
    [100, minPanelWidth],
    [500.4, 500],
    [5_000, maxPanelWidth],
  ])('keeps a panel width of %d within its bounds (%d)', (width, expected) => {
    expect(clampWidth(width)).toBe(expected);
  });

  it('keeps the screen options of the panel', () => {
    const { setOpen, setWidth, setConversation, setAgent, setModel } = useChatStore.getState();

    setOpen(true);
    setWidth(10_000);
    setConversation('c1');
    setAgent('claude-code', 'sonnet');
    setModel('opus');

    expect(useChatStore.getState()).toMatchObject({
      open: true,
      width: maxPanelWidth,
      conversationId: 'c1',
      agent: 'claude-code',
      model: 'opus',
    });
    useChatStore.setState({ open: false, width: defaultPanelWidth, conversationId: null, agent: null, model: null });
  });
});

describe('formatArguments', () => {
  it('prints the arguments of a tool as indented JSON', () => {
    expect(formatArguments({ section: 'apps' })).toBe('{\n  "section": "apps"\n}');
  });

  it('prints empty arguments as an empty object, and never throws on what cannot be serialized', () => {
    expect(formatArguments(undefined)).toBe('{}');
    expect(formatArguments(null)).toBe('{}');
    expect(formatArguments(1n)).toBe('1');
  });
});
