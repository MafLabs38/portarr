import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import MaintenanceIndex from 'Maintenance/MaintenanceIndex';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { maintenanceReport, server } from '__mocks__/server';

describe('MaintenanceIndex', () => {
  it('sums up code and images, and lists what needs attention worst first', async () => {
    renderWithProviders(<MaintenanceIndex />);

    expect(await screen.findByRole('listitem', { name: 'Critical / high: 1' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Dependabot off: 1/2' })).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Major updates: 1' })).toBeInTheDocument();

    const attention = within(screen.getByRole('list', { name: 'Needs attention' })).getAllByRole('listitem');
    expect(attention[0]).toHaveTextContent('1 critical or high vulnerabilities');
    expect(within(attention[0] as HTMLElement).getByRole('link', { name: 'example/shop' })).toHaveAttribute(
      'href',
      'https://github.com/example/shop',
    );
  });

  it('shows my repositories, deployed first, with severities, scanning off and CI', async () => {
    renderWithProviders(<MaintenanceIndex />);

    const running = await screen.findByRole('list', { name: 'Running in the fleet' });
    const shop = within(running).getByRole('listitem', { name: 'example/shop' });
    expect(within(shop).getByRole('link', { name: /^Dependabot\s*1 critical\s*2 medium$/ })).toBeInTheDocument();
    expect(within(shop).getByRole('img', { name: 'Code scanning: off' })).toBeInTheDocument();
    expect(within(shop).getByText('CI failing')).toBeInTheDocument();
    expect(within(shop).getByText('Sonarr')).toBeInTheDocument();

    const elsewhere = screen.getByRole('list', { name: 'Not deployed here (desktop apps, libraries, tools)' });
    expect(within(elsewhere).getByRole('listitem', { name: 'example/desktop' })).toBeInTheDocument();
  });

  it('switches to the deployed images with the keyboard and says when it cannot tell', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    (await screen.findByRole('tab', { name: /My code/ })).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: /Deployed images/ })).toHaveAttribute('aria-selected', 'true');
    const postgres = screen.getByRole('listitem', { name: 'postgres:16-alpine' });
    expect(within(postgres).getByText('Major')).toBeInTheDocument();
    expect(within(postgres).getByText('18-alpine')).toBeInTheDocument();
    const grafana = screen.getByRole('listitem', { name: 'grafana/grafana:latest' });
    expect(within(grafana).getByText('floating tag, no version label: cannot tell')).toBeInTheDocument();
  });

  it('filters the repositories when a figure of the summary is chosen, and clears the filter', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    const tile = await screen.findByRole('listitem', { name: 'Dependabot off: 1/2' });
    await user.click(within(tile).getByRole('link'));

    expect(screen.getByText('Showing only: Dependabot off')).toBeInTheDocument();
    expect(screen.getByText('1 of 2 shown')).toBeInTheDocument();
    expect(within(tile).getByRole('link')).toHaveAttribute('aria-current', 'true');
    expect(screen.queryByRole('listitem', { name: 'example/shop' })).not.toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'example/desktop' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear the filter: Dependabot off' }));

    expect(screen.queryByText(/Showing only/)).not.toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'example/shop' })).toBeInTheDocument();
  });

  it('opens the images tab filtered from a finding of the attention list', async () => {
    renderWithProviders(<MaintenanceIndex />, '/?tab=images&filter=cannot-tell');

    expect(await screen.findByText('Showing only: Cannot tell')).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'grafana/grafana:latest' })).toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: 'postgres:16-alpine' })).not.toBeInTheDocument();
  });

  it('turns an attention line into a link that filters the page', async () => {
    const user = userEvent.setup();
    renderWithProviders(<MaintenanceIndex />);

    const attention = await screen.findByRole('list', { name: 'Needs attention' });
    await user.click(within(attention).getByRole('link', { name: /Dependabot alerts off on 1 repositories/ }));

    expect(screen.getByText('Showing only: Dependabot off')).toBeInTheDocument();
  });

  it('says when the data was read and rescans on request', async () => {
    const user = userEvent.setup();
    let requested = 0;
    server.use(
      http.post('/api/v1/maintenance/refresh', () => {
        requested += 1;
        return HttpResponse.json({}, { status: 202 });
      }),
    );
    renderWithProviders(<MaintenanceIndex />);

    expect(await screen.findByText(/GitHub updated .* ago/)).toBeInTheDocument();
    expect(screen.getByText(/Registries updated .* ago/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(requested).toBe(1));
  });

  it('keeps the refresh button disabled while a refresh is running or just accepted', async () => {
    const running = { ...maintenanceReport, refresh: { ...maintenanceReport.refresh, refreshing: true } };
    server.use(http.get('/api/v1/maintenance', () => HttpResponse.json(running)));
    renderWithProviders(<MaintenanceIndex />);

    const button = await screen.findByRole('button', { name: 'Refreshing…' });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
  });

  it('explains a refused refresh', async () => {
    const user = userEvent.setup();
    server.use(http.post('/api/v1/maintenance/refresh', () => new HttpResponse(null, { status: 429 })));
    renderWithProviders(<MaintenanceIndex />);

    await user.click(await screen.findByRole('button', { name: 'Refresh' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('already started');
  });
});
