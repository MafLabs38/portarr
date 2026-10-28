import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

import type App from 'typings/App';
import type CatalogStatus from 'typings/CatalogStatus';
import type Stack from 'typings/Stack';
import type SystemStatus from 'typings/SystemStatus';
import type Workload from 'typings/Workload';
import type { Environment } from 'typings/Workload';

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

const healthy = { status: 'Healthy', reason: 'Running' } as const;
const down = { status: 'Down', reason: 'Exited (1) 2 minutes ago' } as const;

export const apps: App[] = [
  {
    id: 'sonarr',
    name: 'Sonarr',
    type: 'web',
    icon: 'sonarr',
    description: null,
    tags: [],
    urls: [
      { label: 'Public', href: 'https://sonarr.homelab.example/' },
      { label: 'API', href: 'https://sonarr.homelab.example/api' },
    ],
    stack: { id: 'media', name: 'Media', role: 'critical' },
    status: healthy,
    workloads: [{ environmentId: 'server-1', name: 'sonarr', found: true, status: healthy }],
  },
  {
    id: 'recyclarr',
    name: 'Recyclarr',
    type: 'service',
    icon: null,
    description: null,
    tags: [],
    urls: [],
    stack: { id: 'media', name: 'Media', role: 'optional' },
    status: down,
    workloads: [{ environmentId: 'server-1', name: 'recyclarr', found: true, status: down }],
  },
  {
    id: 'docs',
    name: 'Docs',
    type: 'web',
    icon: null,
    description: null,
    tags: [],
    urls: [{ label: 'docs.homelab.example', href: 'https://docs.homelab.example/' }],
    stack: null,
    status: { status: 'Unknown', reason: 'Not linked to a workload' },
    workloads: [],
  },
];

export const stacks: Stack[] = [
  {
    id: 'media',
    name: 'Media',
    description: null,
    status: { status: 'Degraded', reason: 'Recyclarr not healthy' },
    members: [
      { appId: 'sonarr', name: 'Sonarr', role: 'critical', status: healthy },
      { appId: 'recyclarr', name: 'Recyclarr', role: 'optional', status: down },
    ],
  },
];

export const catalogStatus: CatalogStatus = {
  filePath: '/config/catalog.yaml',
  fileExists: true,
  loadedAt: '2026-01-01T12:00:00Z',
  errors: [],
  apps: 3,
  stacks: 1,
  environments: 2,
};

export const environments: Environment[] = [
  {
    id: 'server-1',
    name: 'Server 1',
    runtime: 'docker',
    reachable: true,
    error: null,
    workloads: 2,
    refreshedAt: null,
  },
  {
    id: 'server-2',
    name: 'Server 2',
    runtime: 'docker',
    reachable: false,
    error: 'Connection refused',
    workloads: 0,
    refreshedAt: null,
  },
];

function container(name: string, status: Workload['status'], appIds: string[]): Workload {
  return {
    environmentId: 'server-1',
    id: `0123456789ab${name}`,
    name,
    image: `${name}:latest`,
    state: status.status === 'Healthy' ? 'running' : 'exited',
    health: 'none',
    statusText: status.reason,
    createdAt: '2026-01-01T12:00:00Z',
    composeProject: 'media',
    appIds,
    status,
    stale: false,
  };
}

export const workloads: Workload[] = [
  container('sonarr', healthy, ['sonarr']),
  container('recyclarr', down, ['recyclarr']),
];

let logLevel = 'Information';

export function resetMockState() {
  logLevel = 'Information';
}

export const handlers = [
  http.get('/api/v1/system/status', () => HttpResponse.json(systemStatus)),
  http.get('/api/v1/apps', () => HttpResponse.json(apps)),
  http.get('/api/v1/stacks', () => HttpResponse.json(stacks)),
  http.get('/api/v1/catalog/status', () => HttpResponse.json(catalogStatus)),
  http.get('/api/v1/environments', () => HttpResponse.json(environments)),
  http.get('/api/v1/workloads', () => HttpResponse.json(workloads)),
  http.get('/api/v1/workloads/:environmentId/:name/usage', () =>
    HttpResponse.json({ cpuCores: 0.004, memoryBytes: 138_522_624, memoryLimitBytes: 536_870_912 }),
  ),
  http.get('/api/v1/system/logging', () => HttpResponse.json({ level: logLevel })),
  http.put('/api/v1/system/logging', async ({ request }) => {
    const body = (await request.json()) as { level: string };
    logLevel = body.level;
    return HttpResponse.json({ level: logLevel });
  }),
];

export const server = setupServer(...handlers);
