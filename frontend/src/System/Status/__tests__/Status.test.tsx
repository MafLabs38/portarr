import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import Status from 'System/Status/Status';

describe('Status', () => {
  it('shows the version and the uptime reported by the API', async () => {
    renderWithProviders(<Status />);

    expect(await screen.findByText('1.2.3')).toBeInTheDocument();
    expect(screen.getByText('1d 2h 3m')).toBeInTheDocument();
    expect(screen.getByText('SQLite 3.46.1')).toBeInTheDocument();
    expect(screen.getByText('/config')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Status' })).toBeInTheDocument();
  });

  it('links to the API reference and the OpenAPI document served by the backend', () => {
    renderWithProviders(<Status />);

    expect(screen.getByRole('link', { name: 'API reference' })).toHaveAttribute('href', '/docs/');
    expect(screen.getByRole('link', { name: 'OpenAPI document' })).toHaveAttribute('href', '/openapi/v1.json');
  });

  it('sets the browser tab title', () => {
    renderWithProviders(<Status />);

    expect(document.title).toBe('Status - Portarr');
  });

  it('shows an error when the API fails', async () => {
    server.use(http.get('/api/v1/system/status', () => new HttpResponse(null, { status: 500 })));

    renderWithProviders(<Status />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load the system status.');
  });
});
