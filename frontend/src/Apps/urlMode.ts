import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import type { AppUrl } from 'typings/App';

export type UrlMode = 'auto' | 'public' | 'lan';

interface UrlModeState {
  mode: UrlMode;
  setMode: (mode: UrlMode) => void;
}

/** Screen option of the launcher, kept per browser (D-46). */
export const useUrlModeStore = create<UrlModeState>()(
  persist((set) => ({ mode: 'auto', setMode: (mode) => set({ mode }) }), {
    name: 'portarr.apps.urlMode',
    storage: createJSONStorage(() => localStorage),
  }),
);

const PRIVATE_HOST =
  /^(localhost|127\.|10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.|\[?f[cd][0-9a-f]{2}:|[^.]+$|.*\.(local|lan|home\.arpa)$)/i;

/** Portarr opened on a LAN address means the browser is on the LAN. */
export function isLanHost(hostname: string): boolean {
  return PRIVATE_HOST.test(hostname);
}

export function resolveScope(mode: UrlMode, hostname: string): 'public' | 'lan' {
  if (mode !== 'auto') {
    return mode;
  }
  return isLanHost(hostname) ? 'lan' : 'public';
}

/** The URL a tile opens: the first one of the preferred scope, otherwise the first one at all. */
export function pickUrl(urls: AppUrl[], scope: 'public' | 'lan'): AppUrl | undefined {
  return urls.find((url) => url.scope === scope) ?? urls[0];
}
