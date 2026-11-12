import { mcpServers } from '__mocks__/server';
import type { McpServer } from 'typings/Mcp';

import describeMcpHealth, { formatElapsed } from '../describeMcpHealth';

const at = (server: McpServer) => describeMcpHealth(server);

describe('describeMcpHealth', () => {
  const [memory, search, github] = mcpServers as [McpServer, McpServer, McpServer];

  it('says a healthy server was seen, with how long ago', () => {
    expect(at(memory)).toMatchObject({ kind: 'success', label: 'Healthy', detail: 'seen 2 min ago' });
  });

  it('says how long a server has been down, why, and when it was last healthy', () => {
    const description = at(search);

    expect(description).toMatchObject({ kind: 'danger', label: 'Down for 12 days' });
    expect(description.detail).toBe('no answer in time · last healthy 12 days ago');
  });

  it('says a server that was never healthy never was', () => {
    const neverSeen: McpServer = {
      ...search,
      health: { ...(search.health as NonNullable<McpServer['health']>), lastHealthyAt: null, error: 'http-502' },
    };

    expect(at(neverSeen).detail).toBe('HTTP 502 · never seen healthy');
  });

  it('is not alarmed by a server that asks for credentials', () => {
    expect(at({ ...memory, status: 'authentication-required' })).toMatchObject({
      kind: 'success',
      label: 'Healthy (asks for credentials)',
    });
  });

  it('does not call a local process down: it is not checked', () => {
    expect(at(github)).toMatchObject({
      kind: 'disabled',
      label: 'Not monitored',
      detail: 'a local process: it runs on the machine that uses it',
    });
  });

  it('waits for the first check of a server just added', () => {
    expect(at({ ...memory, status: 'waiting', health: null })).toMatchObject({ label: 'Waiting for the first check' });
    expect(at({ ...memory, status: 'unknown' })).toMatchObject({
      label: 'Unknown',
      detail: 'Portarr could not run the check',
    });
  });

  it('writes a duration in the largest sensible unit', () => {
    const now = Date.parse('2026-11-12T12:00:00Z');
    expect(formatElapsed('2026-11-12T11:59:30Z', now)).toBe('1 min');
    expect(formatElapsed('2026-11-12T11:15:00Z', now)).toBe('45 min');
    expect(formatElapsed('2026-11-12T07:00:00Z', now)).toBe('5 h');
    expect(formatElapsed('2026-10-31T12:00:00Z', now)).toBe('12 days');
  });
});
