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

  it.each([
    ['Observer', ['apps.view', 'data.view', 'system.view'], ['Apps', 'Data', 'System']],
    ['Guest', ['apps.view'], ['Apps']],
  ])('shows %s only the pages its permissions allow', async (role, permissions, pages) => {
    as(role, permissions);
    renderWithProviders(<PageSidebar />);

    await screen.findByRole('link', { name: pages[0] });

    expect(screen.getAllByRole('link').map((link) => link.textContent)).toEqual(pages);
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
      <Route
        element={
          <RequirePermission permission="users.manage">
            <p>Users page</p>
          </RequirePermission>
        }
        path="/settings/users"
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

  it('keeps the users and roles page from every role that cannot manage users', async () => {
    as('Operator', ['apps.view', 'data.view', 'catalog.write']);
    renderWithProviders(routes, '/settings/users');

    expect(await screen.findByText('Apps page')).toBeInTheDocument();
    expect(screen.queryByText('Users page')).not.toBeInTheDocument();
  });
});
