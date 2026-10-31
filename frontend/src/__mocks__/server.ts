import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

import type App from 'typings/App';
import type CatalogStatus from 'typings/CatalogStatus';
import type { InfrastructureEnvironment } from 'typings/Infrastructure';
import type MaintenanceReport from 'typings/Maintenance';
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
      { label: 'Public', href: 'https://sonarr.homelab.example/', scope: 'public', derived: false },
      { label: 'LAN', href: 'http://10.0.0.10:8989/', scope: 'lan', derived: true },
    ],
    stack: { id: 'media', name: 'Media', role: 'critical' },
    status: healthy,
    workloads: [
      {
        environmentId: 'server-1',
        runtime: 'docker',
        name: 'sonarr',
        role: 'critical',
        found: true,
        state: 'running',
        health: 'healthy',
        statusText: 'Up 3 hours (healthy)',
        status: healthy,
      },
    ],
    check: {
      url: 'https://sonarr.homelab.example/',
      critical: true,
      status: { status: 'Healthy', reason: 'HTTP 302 in 12 ms' },
      latest: {
        at: '2026-01-01T12:00:00Z',
        up: true,
        statusCode: 302,
        latencyMs: 12,
        error: null,
        certificateExpiresAt: null,
      },
    },
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
    workloads: [
      {
        environmentId: 'server-1',
        runtime: 'docker',
        name: 'recyclarr',
        role: 'critical',
        found: true,
        state: 'exited',
        health: 'none',
        statusText: 'Exited (1) 2 minutes ago',
        status: down,
      },
    ],
    check: null,
  },
  {
    id: 'docs',
    name: 'Docs',
    type: 'web',
    icon: null,
    description: null,
    tags: [],
    urls: [{ label: 'docs.homelab.example', href: 'https://docs.homelab.example/', scope: 'public', derived: false }],
    stack: null,
    status: { status: 'Down', reason: 'HTTP 502' },
    workloads: [],
    check: {
      url: 'https://docs.homelab.example/',
      critical: true,
      status: { status: 'Down', reason: 'HTTP 502' },
      latest: {
        at: '2026-01-01T12:00:00Z',
        up: false,
        statusCode: 502,
        latencyMs: 30,
        error: 'http-502',
        certificateExpiresAt: null,
      },
    },
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
  fileName: 'catalog.yaml',
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
    connection: 'connected',
    error: null,
    workloads: 2,
    refreshedAt: null,
  },
  {
    id: 'server-2',
    name: 'Server 2',
    runtime: 'docker',
    connection: 'unreachable',
    error: 'timeout',
    workloads: 1,
    refreshedAt: null,
  },
  {
    id: 'nas',
    name: 'NAS',
    runtime: 'none',
    connection: 'not-connected',
    error: null,
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
    namespace: null,
    node: null,
    appIds,
    status,
    stale: false,
    supportsUsage: true,
    supportsLogs: true,
  };
}

export const workloads: Workload[] = [
  container('sonarr', healthy, ['sonarr']),
  container('recyclarr', down, ['recyclarr']),
  {
    ...container('grafana', { status: 'Unknown', reason: 'server-2 unreachable' }, []),
    environmentId: 'server-2',
    stale: true,
  },
];

/** Server 1 is a Docker host whose usage is the sum of its containers; Server 2 is unreachable. */
export const infrastructure: InfrastructureEnvironment[] = [
  {
    id: 'server-1',
    name: 'Server 1',
    runtime: 'docker',
    sampledAt: '2026-01-01T12:00:00Z',
    error: null,
    intervalSeconds: 3,
    resolutionSeconds: null,
    windowSeconds: 600,
    machines: [
      {
        id: 'server-1',
        name: 'Server 1',
        kind: 'docker-host',
        roles: [],
        ready: null,
        cpuCapacityCores: 8,
        memoryCapacityBytes: 17_179_869_184,
        cpuCores: 1.2,
        memoryBytes: 4_294_967_296,
        scope: 'containers',
        history: [
          { at: '2026-01-01T11:59:54Z', cpuCores: null, memoryBytes: 4_000_000_000, guestCpuCores: null },
          { at: '2026-01-01T11:59:57Z', cpuCores: 0.8, memoryBytes: 4_100_000_000, guestCpuCores: null },
          { at: '2026-01-01T12:00:00Z', cpuCores: 1.2, memoryBytes: 4_294_967_296, guestCpuCores: null },
        ],
        workloads: [
          { id: '0123456789absonarr', cpuCores: 1.1, memoryBytes: 1_073_741_824 },
          { id: '0123456789abrecyclarr', cpuCores: null, memoryBytes: null },
        ],
        host: null,
        guests: [],
      },
    ],
    unplaced: [],
  },
  {
    id: 'server-2',
    name: 'Server 2',
    runtime: 'docker',
    sampledAt: null,
    error: 'timeout',
    intervalSeconds: 3,
    resolutionSeconds: null,
    windowSeconds: 600,
    machines: [],
    unplaced: [],
  },
  {
    id: 'nas',
    name: 'NAS',
    runtime: 'none',
    sampledAt: null,
    error: null,
    intervalSeconds: 3,
    resolutionSeconds: null,
    windowSeconds: 600,
    machines: [],
    unplaced: [],
  },
];

const off = { state: 'off', critical: 0, high: 0, medium: 0, low: 0, other: 0, total: 0, reason: null } as const;

export const maintenanceReport: MaintenanceReport = {
  summary: {
    repositories: 2,
    deployedRepositories: 1,
    critical: 1,
    high: 0,
    medium: 2,
    low: 0,
    qualityAlerts: 0,
    secretAlerts: 0,
    dependabotOff: 1,
    codeScanningOff: 2,
    ciFailing: 1,
    dependabotPullRequests: 3,
    images: 2,
    imagesUpToDate: 0,
    imagesBehind: 1,
    imagesMajor: 1,
    imagesFloating: 1,
    imagesUnknown: 0,
  },
  attention: [
    {
      kind: 'severe-alerts',
      level: 'danger',
      count: 1,
      subjects: [{ label: 'example/shop', url: 'https://github.com/example/shop', count: 1 }],
    },
    {
      kind: 'dependabot-off',
      level: 'warning',
      count: 1,
      subjects: [{ label: 'example/desktop', url: null, count: null }],
    },
  ],
  codeSource: { configured: true, owners: ['example'], scannedAt: '2026-01-01T11:50:00Z', error: null },
  repositories: [
    {
      fullName: 'example/shop',
      owner: 'example',
      name: 'shop',
      url: 'https://github.com/example/shop',
      visibility: 'public',
      language: 'C#',
      description: null,
      pushedAt: '2026-01-01T09:00:00Z',
      defaultBranch: 'main',
      archived: false,
      fork: false,
      dependabot: { state: 'on', critical: 1, high: 0, medium: 2, low: 0, other: 0, total: 3, reason: null },
      codeScanning: off,
      secretScanning: { state: 'on', critical: 0, high: 0, medium: 0, low: 0, other: 0, total: 0, reason: null },
      release: { tag: 'v1.2.0', publishedAt: '2025-12-01T00:00:00Z', url: null, tagOnly: false },
      dependabotPullRequests: 3,
      ci: { workflow: 'CI', status: 'completed', conclusion: 'failure', failed: true, at: null, url: null },
      appIds: ['sonarr'],
      images: [],
      deployed: true,
    },
    {
      fullName: 'example/desktop',
      owner: 'example',
      name: 'desktop',
      url: 'https://github.com/example/desktop',
      visibility: 'private',
      language: 'C#',
      description: null,
      pushedAt: '2025-12-20T09:00:00Z',
      defaultBranch: 'main',
      archived: false,
      fork: false,
      dependabot: off,
      codeScanning: off,
      secretScanning: off,
      release: null,
      dependabotPullRequests: 0,
      ci: null,
      appIds: [],
      images: [],
      deployed: false,
    },
  ],
  images: [
    {
      reference: 'postgres:16-alpine',
      registry: 'docker.io',
      repository: 'library/postgres',
      tag: '16-alpine',
      status: 'behind',
      current: '16-alpine',
      latest: '18-alpine',
      change: 'major',
      currentAt: null,
      latestAt: '2025-12-29T12:00:00Z',
      reason: null,
      fromLabel: false,
      users: [{ environmentId: 'server-1', environmentName: 'Server 1', name: 'app-db', namespace: null, appIds: [] }],
      repositoryFullName: null,
    },
    {
      reference: 'grafana/grafana:latest',
      registry: 'docker.io',
      repository: 'grafana/grafana',
      tag: 'latest',
      status: 'floating',
      current: 'latest',
      latest: null,
      change: null,
      currentAt: null,
      latestAt: null,
      reason: 'floating',
      fromLabel: false,
      users: [{ environmentId: 'server-1', environmentName: 'Server 1', name: 'grafana', namespace: null, appIds: [] }],
      repositoryFullName: null,
    },
  ],
  refresh: {
    gitHubAt: '2026-01-01T11:50:00Z',
    gitHubNextAt: '2026-01-01T12:20:00Z',
    registriesAt: '2026-01-01T09:00:00Z',
    refreshing: false,
    availableAt: null,
  },
};

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
  http.get('/api/v1/infrastructure', () => HttpResponse.json(infrastructure)),
  http.get('/api/v1/maintenance', () => HttpResponse.json(maintenanceReport)),
  http.post('/api/v1/maintenance/refresh', () =>
    HttpResponse.json({ ...maintenanceReport.refresh, refreshing: true }, { status: 202 }),
  ),
  http.get('/api/v1/workloads/:environmentId/:name', ({ params }) => {
    const workload = workloads.find((w) => w.environmentId === params.environmentId && w.name === params.name);
    return workload ? HttpResponse.json(workload) : new HttpResponse(null, { status: 404 });
  }),
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
