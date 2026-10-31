import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import DataIndex from 'Data/DataIndex';

import renderWithProviders from '__mocks__/renderWithProviders';

describe('DataIndex', () => {
  it('sums up what is followed, what is at risk and how many critical assets are off-site', async () => {
    renderWithProviders(<DataIndex />);

    expect(await screen.findByRole('listitem', { name: 'Data followed: 4' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Protected: 1' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'At risk: 2' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Critical with off-site copy: 1/3' })).toBeInTheDocument();
  });

  it('keeps what is not proven yet apart from what is at risk, and says which off-site copies are pending', async () => {
    renderWithProviders(<DataIndex />);

    const notProven = await screen.findByRole('list', { name: 'Not proven yet' });
    expect(within(notProven).getByRole('listitem', { name: 'Waiting for first run: 1' })).toBeInTheDocument();
    expect(within(notProven).getByRole('listitem', { name: 'Not set up: 0' })).toBeInTheDocument();
    expect(within(notProven).getByRole('listitem', { name: 'Unknown: 0' })).toBeInTheDocument();
    expect(screen.getByText('1 pending first run')).toBeInTheDocument();
  });

  it('says which backup tools were read and why one could not be', async () => {
    renderWithProviders(<DataIndex />);

    const tools = await screen.findByRole('list', { name: 'Backup tools read' });
    expect(within(tools).getByText(/4 jobs, read/)).toBeInTheDocument();
    expect(within(tools).getByText(/access refused \(missing permission\)/)).toBeInTheDocument();
  });

  it('groups by importance and app, at-risk first, with a local and an off-site copy per asset', async () => {
    renderWithProviders(<DataIndex />);

    expect(await screen.findByRole('heading', { name: /Critical/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Important/ })).toBeInTheDocument();

    const rows = await screen.findAllByRole('listitem', {
      name: /Shop database|Vault data|Reverse proxy config|Sonarr config/,
    });
    expect(rows.map((row) => row.getAttribute('aria-label'))).toEqual([
      'Shop database',
      'Vault data',
      'Reverse proxy config',
      'Sonarr config',
    ]);

    const shop = rows[0] as HTMLElement;
    expect(within(shop).getByText('PostgreSQL')).toBeInTheDocument();
    expect(within(shop).getByText('within 24 h')).toBeInTheDocument();
    expect(within(within(shop).getByRole('group', { name: 'Local copy' })).getByText('Failed')).toBeInTheDocument();
    expect(
      within(within(shop).getByRole('group', { name: 'Off-site copy' })).getByText('Protected'),
    ).toBeInTheDocument();
    expect(within(shop).getByText('CronJob · dump')).toBeInTheDocument();
    expect(within(shop).getByText('K8up · offsite')).toBeInTheDocument();
  });

  it('shows a copy nothing produces, a tool that is not set up, and a copy that is not followed', async () => {
    renderWithProviders(<DataIndex />);

    const vault = await screen.findByRole('listitem', { name: 'Vault data' });
    expect(
      within(within(vault).getByRole('group', { name: 'Off-site copy' })).getByText('Missing'),
    ).toBeInTheDocument();
    expect(
      within(within(vault).getByRole('group', { name: 'Local copy' })).getByText('Not set up'),
    ).toBeInTheDocument();
    expect(within(vault).getByText('declared, and nothing produces it yet')).toBeInTheDocument();
    expect(within(vault).getByText(/this tool is not configured on the environment/)).toBeInTheDocument();

    const sonarr = screen.getByRole('listitem', { name: 'Sonarr config' });
    expect(within(sonarr).getByRole('group', { name: 'Off-site copy: not followed' })).toBeInTheDocument();
  });

  it('tells a job that never ran from a tool that is not set up, with the next run when the schedule gives it', async () => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-01-01T23:00:00Z') });
    try {
      renderWithProviders(<DataIndex />);

      const proxy = await screen.findByRole('listitem', { name: 'Reverse proxy config' });
      const offsite = within(proxy).getByRole('group', { name: 'Off-site copy' });
      expect(within(offsite).getByText('Waiting for first run')).toBeInTheDocument();
      expect(within(offsite).getByText('has not run yet · next run in 4 h')).toBeInTheDocument();
      expect(within(offsite).getByText(/the job has not run yet/)).toBeInTheDocument();
      expect(
        within(within(proxy).getByRole('group', { name: 'Local copy' })).getByText('Protected'),
      ).toBeInTheDocument();

      const vault = screen.getByRole('listitem', { name: 'Vault data' });
      const local = within(vault).getByRole('group', { name: 'Local copy' });
      expect(within(local).getByText('set up the backup tool on this environment')).toBeInTheDocument();
      expect(within(local).queryByText('Waiting for first run')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('filters the list from a summary figure and clears the filter', async () => {
    const user = userEvent.setup();
    renderWithProviders(<DataIndex />);

    await user.click(await screen.findByRole('link', { name: /Critical with off-site copy/ }));

    expect(screen.queryByRole('listitem', { name: 'Sonarr config' })).not.toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Vault data' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Reverse proxy config' })).toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: 'Shop database' })).not.toBeInTheDocument();
    expect(screen.getByText('2 of 4 shown')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Clear the filter/ }));
    expect(await screen.findByRole('listitem', { name: 'Sonarr config' })).toBeInTheDocument();
  });
});
