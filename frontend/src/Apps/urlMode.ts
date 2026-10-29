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

const IPV4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;
const LOCAL_NAME = /(^localhost$|\.local$|\.lan$|\.home\.arpa$)/i;

function isPrivateIpv4(a: number, b: number): boolean {
  return (
    a === 10 || a === 127 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) || (a === 169 && b === 254)
  );
}

/**
 * Portarr opened on a LAN address means the browser is on the LAN. Same rules as the server's
 * UrlScopes: private, loopback and link-local IPs, IPv6 ULA and link-local, local names, single labels.
 */
export function isLanHost(hostname: string): boolean {
  const host = hostname.replace(/^\[|\]$/g, '').toLowerCase();
  const ipv4 = IPV4.exec(host);
  if (ipv4) {
    return isPrivateIpv4(Number(ipv4[1]), Number(ipv4[2]));
  }
  if (host.includes(':')) {
    return host === '::1' || /^f[cd][0-9a-f]{2}:/.test(host) || /^fe[89ab][0-9a-f]:/.test(host);
  }
  return LOCAL_NAME.test(host) || !host.includes('.');
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
