import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { catalogStatus, server } from '__mocks__/server';
import AppsIndex from 'Apps/AppsIndex';
import { useUrlModeStore } from 'Apps/urlMode';

describe('AppsIndex', () => {
  beforeEach(() => {
    localStorage.clear();
    useUrlModeStore.setState({ mode: 'auto' });
  });

  it('groups apps by stack with the stack status and its reason', async () => {
    renderWithProviders(<AppsIndex />);

    const media = await screen.findByRole('region', { name: 'Media' });
    expect(within(media).getByText('Degraded')).toBeInTheDocument();
    expect(within(media).getByText('· Recyclarr not healthy')).toBeInTheDocument();
    expect(within(media).getByRole('article', { name: 'Sonarr' })).toBeInTheDocument();
    expect(within(media).getByRole('article', { name: 'Recyclarr' })).toBeInTheDocument();
  });

  it('puts apps without a stack in a last section', async () => {
    renderWithProviders(<AppsIndex />);

    const other = await screen.findByRole('region', { name: 'Other apps' });
    expect(within(other).getByRole('article', { name: 'Docs' })).toBeInTheDocument();
  });

  it('opens the LAN URL when Portarr is opened locally, and lists the other URL', async () => {
    renderWithProviders(<AppsIndex />);

    const sonarr = await screen.findByRole('article', { name: 'Sonarr' });
    const launch = within(sonarr).getByRole('link', { name: 'Sonarr' });
    expect(launch).toHaveAttribute('href', 'http://10.0.0.10:8989/');
    expect(launch).toHaveAttribute('target', '_blank');
    expect(within(sonarr).getByRole('link', { name: 'Public' })).toHaveAttribute(
      'href',
      'https://sonarr.homelab.example/',
    );
  });

  it('switches tiles to public URLs', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AppsIndex />);
    const sonarr = await screen.findByRole('article', { name: 'Sonarr' });

    await user.click(screen.getByRole('radio', { name: 'Public' }));

    expect(within(sonarr).getByRole('link', { name: 'Sonarr' })).toHaveAttribute(
      'href',
      'https://sonarr.homelab.example/',
    );
    expect(screen.getByRole('radio', { name: 'Public' })).toHaveAttribute('aria-checked', 'true');
  });

  it('shows the container state and the healthcheck separately', async () => {
    renderWithProviders(<AppsIndex />);

    const sonarr = await screen.findByRole('article', { name: 'Sonarr' });
    expect(within(sonarr).getByText('Up 3 hours')).toBeInTheDocument();
    expect(within(sonarr).getByText('healthy')).toBeInTheDocument();
  });

  it('does not make a service launchable and shows its type', async () => {
    renderWithProviders(<AppsIndex />);

    const recyclarr = await screen.findByRole('article', { name: 'Recyclarr' });
    expect(within(recyclarr).queryByRole('link')).not.toBeInTheDocument();
    expect(within(recyclarr).getByText('Service')).toBeInTheDocument();
    expect(within(recyclarr).getByText('Exited (1) 2 minutes ago')).toBeInTheDocument();
    expect(within(recyclarr).getByText('no healthcheck')).toBeInTheDocument();
  });

  it('shows catalog errors', async () => {
    server.use(
      http.get('/api/v1/catalog/status', () =>
        HttpResponse.json({ ...catalogStatus, errors: ["App 'sonarr': stack 'nope' is not declared in stacks."] }),
      ),
    );

    renderWithProviders(<AppsIndex />);

    expect(await screen.findByRole('alert')).toHaveTextContent("stack 'nope' is not declared");
  });

  it('explains where to declare apps when the catalog is empty', async () => {
    server.use(
      http.get('/api/v1/apps', () => HttpResponse.json([])),
      http.get('/api/v1/stacks', () => HttpResponse.json([])),
    );

    renderWithProviders(<AppsIndex />);

    expect(await screen.findByText('Ahoy! No apps on board yet.')).toBeInTheDocument();
    expect(screen.getByText(/\/config\/catalog\.yaml/)).toBeInTheDocument();
  });
});
