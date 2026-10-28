import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

import type SystemStatus from 'typings/SystemStatus';

export const systemStatus: SystemStatus = {
  appName: 'Portarr',
  version: '1.2.3',
  startTime: '2026-01-01T12:00:00Z',
  uptimeSeconds: 93_780,
  runtimeVersion: '.NET 10.0.0',
  osDescription: 'Linux 6.8.0',
  databaseEngine: 'SQLite 3.46.1',
  dataFolder: '/config',
};

let logLevel = 'Information';

export function resetMockState() {
  logLevel = 'Information';
}

export const handlers = [
  http.get('/api/v1/system/status', () => HttpResponse.json(systemStatus)),
  http.get('/api/v1/system/logging', () => HttpResponse.json({ level: logLevel })),
  http.put('/api/v1/system/logging', async ({ request }) => {
    const body = (await request.json()) as { level: string };
    logLevel = body.level;
    return HttpResponse.json({ level: logLevel });
  }),
];

export const server = setupServer(...handlers);
