import { screen, within } from '@testing-library/react';

import renderWithProviders from '__mocks__/renderWithProviders';
import WorkloadsIndex from 'Workloads/WorkloadsIndex';

describe('WorkloadsIndex', () => {
  it('lists containers per environment with their status and app', async () => {
    renderWithProviders(<WorkloadsIndex />);

    const server = await screen.findByRole('region', { name: 'Server 1' });
    const sonarr = await within(server).findByRole('link', { name: 'sonarr' });
    expect(sonarr).toHaveAttribute('href', '/workloads/server-1/sonarr');
    const row = within(server).getByRole('row', { name: /sonarr/ });
    expect(row).toHaveTextContent('Healthy');
    expect(row).toHaveAttribute('data-stale', 'false');
  });

  it('keeps the last known containers of an unreachable environment and says why', async () => {
    renderWithProviders(<WorkloadsIndex />);

    const server = await screen.findByRole('region', { name: 'Server 2' });
    expect(within(server).getByRole('alert')).toHaveTextContent('Unreachable (timed out)');
    expect(await within(server).findByRole('row', { name: /grafana/ })).toHaveAttribute('data-stale', 'true');
  });

  it('shows an environment without connector as not connected, not as an error', async () => {
    renderWithProviders(<WorkloadsIndex />);

    const nas = await screen.findByRole('region', { name: 'NAS' });
    expect(within(nas).queryByRole('alert')).not.toBeInTheDocument();
    expect(within(nas).getByText(/Not connected: declare a Docker endpoint/)).toBeInTheDocument();
  });
});
