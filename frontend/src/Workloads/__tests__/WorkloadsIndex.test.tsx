import { screen, within } from '@testing-library/react';

import renderWithProviders from '__mocks__/renderWithProviders';
import WorkloadsIndex from 'Workloads/WorkloadsIndex';

describe('WorkloadsIndex', () => {
  it('lists containers per environment with their status and app', async () => {
    renderWithProviders(<WorkloadsIndex />);

    const server = await screen.findByRole('region', { name: 'Server 1' });
    const sonarr = await within(server).findByRole('link', { name: 'sonarr' });
    expect(sonarr).toHaveAttribute('href', '/workloads/server-1/sonarr');

    const row = sonarr.closest('tr');
    if (!row) {
      throw new Error('sonarr row not found');
    }
    expect(within(row).getByText('Healthy')).toBeInTheDocument();
    expect(within(row).getByText('sonarr:latest')).toBeInTheDocument();
  });

  it('warns when an environment is unreachable', async () => {
    renderWithProviders(<WorkloadsIndex />);

    const server = await screen.findByRole('region', { name: 'Server 2' });
    expect(within(server).getByRole('alert')).toHaveTextContent('Connection refused');
  });
});
