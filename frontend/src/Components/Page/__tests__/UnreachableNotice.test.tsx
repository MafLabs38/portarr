import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { environments, server } from '__mocks__/server';
import UnreachableNotice from 'Components/Page/UnreachableNotice';

describe('UnreachableNotice', () => {
  it('explains in plain words that an unreachable machine is not a failure of its apps', async () => {
    renderWithProviders(<UnreachableNotice />);

    expect(await screen.findByText('Portarr cannot read Server 2.')).toBeInTheDocument();
    expect(screen.getByText(/not a failure of your apps/)).toBeInTheDocument();
    expect(screen.getByText(/a firewall drops the requests/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'How to check' })).toHaveAttribute(
      'href',
      expect.stringContaining('troubleshooting.md#a-machine-is-unreachable'),
    );
  });

  it('names every unreachable machine', async () => {
    server.use(
      http.get('/api/v1/environments', () =>
        HttpResponse.json(environments.map((environment) => ({ ...environment, connection: 'unreachable' }))),
      ),
    );
    renderWithProviders(<UnreachableNotice />);

    expect(await screen.findByText('Portarr cannot read 3 machines: Server 1, Server 2, NAS.')).toBeInTheDocument();
  });

  it('says nothing while every environment is readable', async () => {
    server.use(
      http.get('/api/v1/environments', () =>
        HttpResponse.json(environments.filter((environment) => environment.connection !== 'unreachable')),
      ),
    );
    const { container } = renderWithProviders(<UnreachableNotice />);

    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(container).toBeEmptyDOMElement();
  });
});
