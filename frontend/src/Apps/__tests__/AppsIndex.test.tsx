import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { catalogStatus, server } from '__mocks__/server';
import AppsIndex from 'Apps/AppsIndex';

describe('AppsIndex', () => {
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

  it('opens a web app on its first URL in a new tab and lists the others', async () => {
    renderWithProviders(<AppsIndex />);

    const sonarr = await screen.findByRole('article', { name: 'Sonarr' });
    const launch = within(sonarr).getByRole('link', { name: 'Sonarr' });
    expect(launch).toHaveAttribute('href', 'https://sonarr.homelab.example/');
    expect(launch).toHaveAttribute('target', '_blank');
    expect(within(sonarr).getByRole('link', { name: 'API' })).toHaveAttribute(
      'href',
      'https://sonarr.homelab.example/api',
    );
  });

  it('does not make a service launchable and shows its type', async () => {
    renderWithProviders(<AppsIndex />);

    const recyclarr = await screen.findByRole('article', { name: 'Recyclarr' });
    expect(within(recyclarr).queryByRole('link')).not.toBeInTheDocument();
    expect(within(recyclarr).getByText('Service')).toBeInTheDocument();
    expect(within(recyclarr).getByText('Down')).toBeInTheDocument();
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
