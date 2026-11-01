import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

export const minPanelWidth = 320;
export const maxPanelWidth = 960;
export const defaultPanelWidth = 440;

interface ChatState {
  open: boolean;
  width: number;
  /** The conversation shown; null for a new one. */
  conversationId: string | null;
  agent: string | null;
  model: string | null;
  setOpen: (open: boolean) => void;
  setWidth: (width: number) => void;
  setConversation: (conversationId: string | null) => void;
  setAgent: (agent: string, model: string) => void;
  setModel: (model: string) => void;
}

export function clampWidth(width: number): number {
  return Math.round(Math.min(maxPanelWidth, Math.max(minPanelWidth, width)));
}

/** Screen options of the chat panel (D-90), kept per browser. */
export const useChatStore = create<ChatState>()(
  persist(
    (set) => ({
      open: false,
      width: defaultPanelWidth,
      conversationId: null,
      agent: null,
      model: null,
      setOpen: (open) => set({ open }),
      setWidth: (width) => set({ width: clampWidth(width) }),
      setConversation: (conversationId) => set({ conversationId }),
      setAgent: (agent, model) => set({ agent, model }),
      setModel: (model) => set({ model }),
    }),
    { name: 'portarr.chat', storage: createJSONStorage(() => localStorage) },
  ),
);
