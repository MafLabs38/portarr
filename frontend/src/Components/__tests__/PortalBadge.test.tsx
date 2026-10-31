import { fireEvent, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { portal, server } from '__mocks__/server';
import PortalBadge from 'Components/Page/PortalBadge';

describe('PortalBadge', () => {
  it('shows the name, the version of the running Portarr and the porthole when no logo is set', async () => {
    const { container } = renderWithProviders(<PortalBadge />);

    expect(await screen.findByText('Homelab')).toBeInTheDocument();
    expect(screen.getByText('v1.2.3')).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('svg')).toBeInTheDocument();
  });

  it('colors the name and the version with Portarr accent by default', async () => {
    renderWithProviders(<PortalBadge />);

    expect(await screen.findByText('Homelab')).toHaveStyle({ color: 'var(--accent)' });
    expect(screen.getByText('v1.2.3')).toHaveStyle({ color: 'var(--accent)' });
  });

  it('uses the colors of the catalog: a hex color as it is, a theme token as its variable', async () => {
    server.use(
      http.get('/api/v1/portal', () =>
        HttpResponse.json({ ...portal, colors: { name: '#abc123', version: 'inkMuted' } }),
      ),
    );
    renderWithProviders(<PortalBadge />);

    expect(await screen.findByText('Homelab')).toHaveStyle({ color: '#abc123' });
    expect(screen.getByText('v1.2.3')).toHaveStyle({ color: 'var(--inkMuted)' });
  });

  it('never lets an unknown color through', async () => {
    server.use(
      http.get('/api/v1/portal', () =>
        HttpResponse.json({ ...portal, colors: { name: 'red; background: url(x)', version: 'primary' } }),
      ),
    );
    renderWithProviders(<PortalBadge />);

    expect(await screen.findByText('Homelab')).toHaveStyle({ color: 'var(--accent)' });
    expect(screen.getByText('v1.2.3')).toHaveStyle({ color: 'var(--accent)' });
  });

  it('shows the operator logo with the name as its alternative text', async () => {
    server.use(
      http.get('/api/v1/portal', () => HttpResponse.json({ ...portal, logoUrl: '/api/v1/portal/logo?v=abc123' })),
    );
    renderWithProviders(<PortalBadge />);

    const logo = await screen.findByRole('img', { name: 'Homelab' });

    expect(logo).toHaveAttribute('src', '/api/v1/portal/logo?v=abc123');
  });

  it('falls back to the porthole when the logo cannot be loaded', async () => {
    server.use(
      http.get('/api/v1/portal', () => HttpResponse.json({ ...portal, logoUrl: '/api/v1/portal/logo?v=abc123' })),
    );
    const { container } = renderWithProviders(<PortalBadge />);

    fireEvent.error(await screen.findByRole('img', { name: 'Homelab' }));

    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText('Homelab')).toBeInTheDocument();
  });

  it('shows Portarr itself when the identity cannot be read', async () => {
    server.use(http.get('/api/v1/portal', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<PortalBadge />);

    expect(await screen.findByText('Portarr')).toBeInTheDocument();
  });
});
