import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, server } from '__mocks__/server';
import RequirePermission from 'Auth/RequirePermission';
import { CATALOG_WRITE, USERS_MANAGE } from 'Helpers/Props/permissions';
import SettingsHome from 'Settings/SettingsHome';
import SettingsNav from 'Settings/SettingsNav';

const as = (permissions: string[]) =>
  server.use(http.get('/api/v1/auth/me', () => HttpResponse.json({ ...me, permissions })));

const routes = (
  <Routes>
    <Route
      element={
        <RequirePermission permission={[USERS_MANAGE, CATALOG_WRITE]}>
          <SettingsHome />
        </RequirePermission>
      }
      path="/settings"
    />
    <Route
      element={
        <>
          <p>General page</p>
          <SettingsNav />
        </>
      }
      path="/settings/general"
    />
    <Route
      element={
        <>
          <p>Users page</p>
          <SettingsNav />
        </>
      }
      path="/settings/users"
    />
    <Route element={<p>Apps page</p>} path="/apps" />
  </Routes>
);

describe('Settings', () => {
  it('opens General first for a role that changes the catalog and manages users, and links both pages', async () => {
    as(['apps.view', 'catalog.write', 'users.manage']);
    renderWithProviders(routes, '/settings');

    expect(await screen.findByText('General page')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'General' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Users & Auth' })).toHaveAttribute('href', '/settings/users');
  });

  it('opens the only page a role may open, with no links to choose from', async () => {
    as(['apps.view', 'users.manage']);
    renderWithProviders(routes, '/settings');

    expect(await screen.findByText('Users page')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('keeps a role that does neither out of Settings', async () => {
    as(['apps.view']);
    renderWithProviders(routes, '/settings');

    expect(await screen.findByText('Apps page')).toBeInTheDocument();
  });
});
