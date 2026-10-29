import { isLanHost, pickUrl, resolveScope, useUrlModeStore } from 'Apps/urlMode';
import type { AppUrl } from 'typings/App';

const publicUrl: AppUrl = { label: 'Public', href: 'https://app.homelab.example/', scope: 'public', derived: false };
const lanUrl: AppUrl = { label: 'LAN', href: 'http://10.0.0.10:8096/', scope: 'lan', derived: true };

describe('urlMode', () => {
  it.each([
    ['192.168.100.20', true],
    ['10.0.0.10', true],
    ['169.254.1.1', true],
    ['[fd00::1]', true],
    ['fe80::1', true],
    ['10.homelab.example', false],
    ['127.example.org', false],
    ['172.16.4.2', true],
    ['localhost', true],
    ['portarr', true],
    ['nas.local', true],
    ['portarr.homelab.example', false],
    ['172.40.0.1', false],
  ])('treats %s as LAN: %s', (host, expected) => {
    expect(isLanHost(host)).toBe(expected);
  });

  it('follows the address Portarr is opened on in auto mode', () => {
    expect(resolveScope('auto', '192.168.100.20')).toBe('lan');
    expect(resolveScope('auto', 'portarr.homelab.example')).toBe('public');
    expect(resolveScope('public', '192.168.100.20')).toBe('public');
  });

  it('falls back to any URL when the preferred scope has none', () => {
    expect(pickUrl([publicUrl, lanUrl], 'lan')).toBe(lanUrl);
    expect(pickUrl([lanUrl], 'public')).toBe(lanUrl);
    expect(pickUrl([], 'public')).toBeUndefined();
  });

  it('restores the chosen mode from the browser storage', async () => {
    localStorage.setItem('portarr.apps.urlMode', JSON.stringify({ state: { mode: 'lan' }, version: 0 }));

    await useUrlModeStore.persist.rehydrate();

    expect(useUrlModeStore.getState().mode).toBe('lan');
    localStorage.clear();
    useUrlModeStore.setState({ mode: 'auto' });
  });
});
