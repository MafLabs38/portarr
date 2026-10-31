import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, server } from '__mocks__/server';
import RequirePermission from 'Auth/RequirePermission';
import PageSidebar from 'Components/Page/PageSidebar';

const as = (role: string, permissions: string[]) =>
  server.use(http.get('/api/v1/auth/me', () => HttpResponse.json({ ...me, role, permissions })));

describe('PageSidebar', () => {
  it('shows every page to an administrator, Settings included', async () => {
    renderWithProviders(<PageSidebar />);

    expect(await screen.findByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings/users');
    expect(screen.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Apps',
      'Infrastructure',
      'Data',
      'Maintenance',
      'Assistant',
      'System',
      'Settings',
    ]);
  });

  it('shows an observer the view pages only', async () => {
    as('Observer', ['apps.view', 'data.view', 'system.view']);
    renderWithProviders(<PageSidebar />);

    await screen.findByRole('link', { name: 'Apps' });

    expect(screen.getAllByRole('link').map((link) => link.textContent)).toEqual(['Apps', 'Data', 'System']);
  });

  it('shows a guest the apps alone', async () => {
    as('Guest', ['apps.view']);
    renderWithProviders(<PageSidebar />);

    await screen.findByRole('link', { name: 'Apps' });

    expect(screen.getAllByRole('link')).toHaveLength(1);
  });
});

describe('RequirePermission', () => {
  const routes = (
    <Routes>
      <Route
        element={
          <RequirePermission permission="data.view">
            <p>Data page</p>
          </RequirePermission>
        }
        path="/data"
      />
      <Route element={<p>Apps page</p>} path="/apps" />
    </Routes>
  );

  it('opens the page of a role that holds the permission', async () => {
    renderWithProviders(routes, '/data');

    expect(await screen.findByText('Data page')).toBeInTheDocument();
  });

  it('sends a role without it to the first page it may see', async () => {
    as('Guest', ['apps.view']);
    renderWithProviders(routes, '/data');

    expect(await screen.findByText('Apps page')).toBeInTheDocument();
    expect(screen.queryByText('Data page')).not.toBeInTheDocument();
  });
});
