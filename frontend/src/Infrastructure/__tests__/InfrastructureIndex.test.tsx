import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import InfrastructureIndex from 'Infrastructure/InfrastructureIndex';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { infrastructure, server, workloads } from '__mocks__/server';
import type { HostMetrics, InfrastructureEnvironment, Machine } from 'typings/Infrastructure';
import type Workload from 'typings/Workload';

// jsdom has no canvas: the charts render as their accessible element only.
vi.mock('react-chartjs-2', () => ({
  Line: ({ 'aria-label': label }: { 'aria-label': string }) => <canvas aria-label={label} role="img" />,
  Doughnut: ({ 'aria-label': label }: { 'aria-label': string }) => <canvas aria-label={label} role="img" />,
}));

describe('InfrastructureIndex', () => {
  it('shows a machine with its live CPU chart, load and memory ring, labelled as the sum of its containers', async () => {
    renderWithProviders(<InfrastructureIndex />);

    const machine = await screen.findByRole('article', { name: 'Server 1' });
    expect(within(machine).getByText('15 %')).toBeInTheDocument();
    expect(within(machine).getByText('1.20 of 8 cores')).toBeInTheDocument();
    expect(within(machine).getByRole('img', { name: /CPU of Server 1 over the last 10 minutes/ })).toBeInTheDocument();
    expect(within(machine).getByRole('img', { name: 'Memory of Server 1: 4.0 GB of 16.0 GB' })).toBeInTheDocument();
    expect(within(machine).getByText('25 %')).toBeInTheDocument();
    expect(within(machine).getAllByText(/Containers/).length).toBeGreaterThan(0);
    expect(within(machine).getByText('Docker host · 8 cores · 16.0 GB')).toBeInTheDocument();
    expect(within(machine).getByText(/every 3 s/)).toBeInTheDocument();
  });

  it('shows the age of the last sample, live or stale', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-01-01T12:00:02Z'));
    try {
      renderWithProviders(<InfrastructureIndex />);
      const machine = await screen.findByRole('article', { name: 'Server 1' });
      expect(within(machine).getByText('Live · 2 s ago')).toBeInTheDocument();

      vi.setSystemTime(new Date('2026-01-01T12:02:00Z'));
      expect(await within(machine).findByText('Stale · 2 min ago', {}, { timeout: 2_000 })).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('lists only running consumers, scaled to the heaviest, with their share of the machine', async () => {
    renderWithProviders(<InfrastructureIndex />);

    const machine = await screen.findByRole('article', { name: 'Server 1' });
    const sonarr = await within(machine).findByRole('link', { name: /sonarr/ });
    expect(sonarr).toHaveAttribute('href', '/workloads/server-1/sonarr');
    expect(within(sonarr).getByText('14 % · 1.10 vCPU')).toBeInTheDocument();
    expect(within(sonarr).getByText('1.0 GB · 6 %')).toBeInTheDocument();
    // The heaviest consumer fills its bar: bars compare consumers, the text gives the share of the machine.
    expect(within(sonarr).getByRole('progressbar', { name: 'CPU of sonarr' })).toHaveAttribute('aria-valuenow', '100');
    expect(within(machine).queryByRole('link', { name: /recyclarr/ })).not.toBeInTheDocument();
  });

  it('stacks a host-agent machine: CPU with its chart and cores, then memory, then swap', async () => {
    const environment = infrastructure[0] as InfrastructureEnvironment;
    const machine0 = environment.machines[0] as Machine;
    const gb = 1_073_741_824;
    const hostMetrics: HostMetrics = {
      environmentId: environment.id,
      machineId: machine0.id,
      source: 'node-exporter',
      sampledAt: new Date().toISOString(),
      error: null,
      intervalSeconds: 1,
      cores: 8,
      cpuPercent: 39,
      iowaitPercent: 1.3,
      corePercents: [10, 20, 30, 40],
      load1: 6.65,
      temperatureCelsius: 54,
      memoryTotalBytes: 16 * gb,
      memoryUsedBytes: 8 * gb,
      memoryReclaimableBytes: 2 * gb,
      memoryFreeBytes: 6 * gb,
      swapTotalBytes: 4 * gb,
      swapUsedBytes: gb,
      history: [],
    };
    server.use(
      http.get('/api/v1/infrastructure', () =>
        HttpResponse.json([{ ...environment, machines: [{ ...machine0, hostMetrics }] }]),
      ),
    );
    renderWithProviders(<InfrastructureIndex />);

    const card = await screen.findByRole('article', { name: 'Server 1' });
    const cpu = within(card).getByRole('region', { name: 'CPU' });
    const memory = within(card).getByRole('region', { name: 'Memory' });
    expect(within(cpu).getByText('39 %', { selector: 'span' })).toBeInTheDocument();
    expect(within(cpu).getByText('I/O wait 1.3 %')).toBeInTheDocument();
    expect(within(cpu).getByText('load avg 6.65 · CPU 54 °C')).toBeInTheDocument();
    expect(within(cpu).getByRole('img', { name: /CPU of Server 1 over the last/ })).toBeInTheDocument();
    expect(within(cpu).getAllByRole('listitem').length).toBeGreaterThanOrEqual(4);
    expect(within(memory).getByRole('img', { name: /Memory of Server 1/ })).toBeInTheDocument();
    // CPU, then memory, then swap, in document order.
    expect(cpu.compareDocumentPosition(memory) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      memory.compareDocumentPosition(within(card).getByText('Swap')) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('shows a hypervisor with its VMs as they report themselves, followed by their cards', async () => {
    const environment = infrastructure[0] as InfrastructureEnvironment;
    const machine0 = environment.machines[0] as Machine;
    const gb = 1_073_741_824;
    const hypervisor: InfrastructureEnvironment = {
      ...environment,
      id: 'hypervisor',
      name: 'Hypervisor',
      machines: [
        {
          ...machine0,
          id: 'hypervisor',
          name: 'Hypervisor',
          cpuCores: 0.5,
          memoryBytes: 8 * gb,
          memoryCapacityBytes: 64 * gb,
          workloads: [],
          guests: [
            {
              environmentId: 'vm-1',
              machineId: 'vm-1',
              name: 'VM 1',
              cpuCapacityCores: 4,
              memoryCapacityBytes: 12 * gb,
              cpuCores: 0.5,
              scope: 'containers',
            },
            {
              environmentId: 'cluster',
              machineId: 'node-1',
              name: 'node-1',
              cpuCapacityCores: 4,
              memoryCapacityBytes: 20 * gb,
              cpuCores: 1,
              scope: 'host',
            },
          ],
        },
      ],
    };
    const vm: InfrastructureEnvironment = {
      ...environment,
      id: 'vm-1',
      name: 'VM 1',
      machines: [
        {
          ...machine0,
          id: 'vm-1',
          name: 'VM 1',
          workloads: [],
          host: { environmentId: 'hypervisor', name: 'Hypervisor' },
        },
      ],
    };
    // The VM comes first in the catalog: its card still follows its hypervisor.
    server.use(http.get('/api/v1/infrastructure', () => HttpResponse.json([vm, hypervisor])));
    renderWithProviders(<InfrastructureIndex />);

    const card = await screen.findByRole('article', { name: 'Hypervisor' });
    expect(within(card).getByText('Hypervisor · 2 VMs')).toBeInTheDocument();
    expect(within(card).getByText('≥ 25 %')).toBeInTheDocument();
    expect(within(card).getByText('VMs (guest-reported) ≥ 1.50 vCPU')).toBeInTheDocument();
    expect(within(card).getByText('32.0 GB')).toBeInTheDocument();
    expect(within(card).getByText('24.0 GB')).toBeInTheDocument();
    expect(within(card).getByText('63 %')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: /VM 1/ })).toHaveAttribute('href', '#machine-vm-1-vm-1');
    expect(within(card).getByText('≥ 6 % · 0.50 vCPU (containers only)')).toBeInTheDocument();

    const guest = screen.getByRole('article', { name: 'VM 1' });
    expect(within(guest).getByText('VM on Hypervisor')).toBeInTheDocument();
    expect(screen.getAllByRole('article').map((a) => a.getAttribute('aria-label'))).toEqual(['Hypervisor', 'VM 1']);
  });

  it('shows the top consumers first and all running ones on demand', async () => {
    const base = workloads[0] as Workload;
    const environment = infrastructure[0] as InfrastructureEnvironment;
    const machine0 = environment.machines[0] as Machine;
    const many: Workload[] = Array.from({ length: 10 }, (_, i) => ({
      ...base,
      id: `c${i}`,
      name: `app-${i}`,
    }));
    server.use(
      http.get('/api/v1/workloads', () => HttpResponse.json(many)),
      http.get('/api/v1/infrastructure', () =>
        HttpResponse.json([
          {
            ...environment,
            machines: [
              {
                ...machine0,
                workloads: many.map((w, i) => ({ id: w.id, cpuCores: 1 - i / 10, memoryBytes: 1000 })),
              },
            ],
          },
        ]),
      ),
    );
    renderWithProviders(<InfrastructureIndex />);

    const machine = await screen.findByRole('article', { name: 'Server 1' });
    expect(await within(machine).findAllByRole('link')).toHaveLength(8);
    expect(within(machine).getAllByRole('link')[0]).toHaveTextContent('app-0');

    await userEvent.click(within(machine).getByRole('button', { name: 'Show all running (10)' }));
    expect(within(machine).getAllByRole('link')).toHaveLength(10);
  });

  it('says why an environment has no live metrics', async () => {
    renderWithProviders(<InfrastructureIndex />);

    const unreachable = await screen.findByRole('listitem', { name: 'Server 2' });
    expect(unreachable).toHaveTextContent('Docker · Live metrics unavailable: timed out.');
    expect(screen.getByRole('listitem', { name: 'NAS' })).toHaveTextContent(
      'No connector · No connector declared: nothing to measure.',
    );
  });
});
