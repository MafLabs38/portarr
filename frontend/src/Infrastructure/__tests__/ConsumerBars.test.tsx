import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ConsumerBars from 'Infrastructure/ConsumerBars';
import { useConsumerSortStore } from 'Infrastructure/consumerSortStore';
import { consumers } from 'Infrastructure/machineUsage';

import renderWithProviders from '__mocks__/renderWithProviders';
import type { Machine } from 'typings/Infrastructure';
import type Workload from 'typings/Workload';

const GB = 1_073_741_824;

const workload = (id: string, name: string) => ({ environmentId: 'env', id, name, state: 'running' }) as Workload;
const workloads = new Map<string, Workload>([
  ['a', workload('a', 'cpu-hog')],
  ['b', workload('b', 'ram-hog')],
  ['c', workload('c', 'idle')],
]);
const machine = {
  workloads: [
    { id: 'a', cpuCores: 2, memoryBytes: 1 * GB },
    { id: 'b', cpuCores: 0.1, memoryBytes: 8 * GB },
    { id: 'c', cpuCores: 0, memoryBytes: 0.1 * GB },
  ],
} as Machine;

function Bars() {
  const { sort, setSort } = useConsumerSortStore();
  return (
    <ConsumerBars
      consumers={consumers(machine, workloads, sort)}
      cpuCapacity={8}
      memoryCapacity={16 * GB}
      onSortChange={setSort}
      sort={sort}
    />
  );
}

const names = () =>
  within(screen.getByRole('list'))
    .getAllByRole('link')
    .map((link) => /^[a-z-]+/.exec(link.textContent ?? '')?.[0]);

describe('the top consumers of a machine', () => {
  beforeEach(() => {
    localStorage.clear();
    useConsumerSortStore.setState({ sort: 'cpu' });
  });

  it('ranks by CPU by default and by memory when the operator asks, and remembers the choice', async () => {
    const user = userEvent.setup();
    renderWithProviders(<Bars />);

    expect(names()).toEqual(['cpu-hog', 'ram-hog', 'idle']);
    expect(screen.getByRole('button', { name: 'CPU' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'RAM' }));

    expect(names()).toEqual(['ram-hog', 'cpu-hog', 'idle']);
    expect(screen.getByRole('button', { name: 'RAM' })).toHaveAttribute('aria-pressed', 'true');
    expect(JSON.parse(localStorage.getItem('portarr.infrastructure') ?? '{}').state.sort).toBe('memory');
  });

  it('starts from the choice kept in the browser', () => {
    localStorage.setItem('portarr.infrastructure', JSON.stringify({ state: { sort: 'memory' }, version: 0 }));
    useConsumerSortStore.persist.rehydrate();

    renderWithProviders(<Bars />);

    expect(names()[0]).toBe('ram-hog');
  });
});

describe('consumers', () => {
  it('breaks a tie on the chosen resource by the other one', () => {
    const tied = {
      workloads: [
        { id: 'a', cpuCores: 1, memoryBytes: 1 * GB },
        { id: 'b', cpuCores: 1, memoryBytes: 4 * GB },
      ],
    } as Machine;

    expect(consumers(tied, workloads, 'cpu').map((c) => c.workload.id)).toEqual(['b', 'a']);
    expect(consumers(tied, workloads, 'memory').map((c) => c.workload.id)).toEqual(['b', 'a']);
  });
});
