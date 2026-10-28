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
    expect(screen.getByRole('heading', { level: 1, name: 'Status' })).toBeInTheDocument();
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
