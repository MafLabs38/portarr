import { render, screen } from '@testing-library/react';

import WorkloadSummary from 'Apps/WorkloadSummary';
import type { AppWorkload } from 'typings/App';

function workload(name: string, overrides: Partial<AppWorkload>): AppWorkload {
  return {
    environmentId: 'server-1',
    name,
    role: 'critical',
    found: true,
    state: 'running',
    health: 'none',
    statusText: 'Up 2 days',
    status: { status: 'Healthy', reason: 'Running' },
    ...overrides,
  };
}

describe('WorkloadSummary', () => {
  it('counts containers and unhealthy ones for an app made of several services', () => {
    render(
      <WorkloadSummary
        workloads={[
          workload('front', { health: 'healthy', statusText: 'Up 2 days (healthy)' }),
          workload('api', {}),
          workload('scanner', { health: 'unhealthy', statusText: 'Up 1 hour (unhealthy)' }),
          workload('job', { state: 'exited', statusText: 'Exited (1) 1 hour ago' }),
        ]}
      />,
    );

    expect(screen.getByText('3/4 up')).toBeInTheDocument();
    expect(screen.getByText('1 unhealthy')).toBeInTheDocument();
    expect(screen.getByTitle(/scanner: Up 1 hour \(unhealthy\)/)).toBeInTheDocument();
  });

  it('reports starting healthchecks before calling the app healthy', () => {
    render(
      <WorkloadSummary workloads={[workload('a', { health: 'healthy' }), workload('b', { health: 'starting' })]} />,
    );

    expect(screen.getByText('1 starting')).toBeInTheDocument();
  });

  it('says when a declared container does not exist', () => {
    render(<WorkloadSummary workloads={[workload('ghost', { found: false, state: null, statusText: null })]} />);

    expect(screen.getByText('Not found')).toBeInTheDocument();
  });

  it('shows a starting healthcheck', () => {
    render(
      <WorkloadSummary
        workloads={[workload('db', { health: 'starting', statusText: 'Up 5 seconds (health: starting)' })]}
      />,
    );

    expect(screen.getByText('Up 5 seconds')).toBeInTheDocument();
    expect(screen.getByText('starting')).toBeInTheDocument();
  });
});
