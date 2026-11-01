import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import UsersAuthPage from 'Settings/UsersAuth/UsersAuthPage';
import type { Role, RoleList, UserAccount } from 'typings/Auth';

const roles: Role[] = [
  {
    id: 1,
    name: 'Admin',
    description: 'Everything.',
    builtIn: true,
    permissions: ['apps.view', 'users.manage'],
    users: 1,
  },
  { id: 3, name: 'Observer', description: null, builtIn: true, permissions: ['apps.view'], users: 1 },
  {
    id: 5,
    name: 'Viewer',
    description: 'Apps and data',
    builtIn: false,
    permissions: ['apps.view', 'data.view'],
    users: 0,
  },
];

const list: RoleList = {
  roles,
  permissions: [
    { key: 'apps.view', area: 'Apps', reserved: false },
    { key: 'data.view', area: 'Data', reserved: false },
    { key: 'maintenance.view', area: 'Maintenance', reserved: false },
    { key: 'users.manage', area: 'Administration', reserved: true },
    { key: 'secrets.view', area: 'Administration', reserved: true },
  ],
};

const user = (over: Partial<UserAccount>): UserAccount => ({
  id: 1,
  username: 'admin',
  displayName: 'Ada Admin',
  roleId: 1,
  role: 'Admin',
  avatarUrl: null,
  created: '2026-01-01T12:00:00Z',
  lastLogin: null,
  disabled: false,
  lockedUntil: null,
  ...over,
});

const users = [
  user({}),
  user({ id: 2, username: 'olivia', displayName: 'Olivia', roleId: 3, role: 'Observer', disabled: true }),
];

beforeEach(() =>
  server.use(
    http.get('/api/v1/users', () => HttpResponse.json(users)),
    http.get('/api/v1/roles', () => HttpResponse.json(list)),
    http.get('/api/v1/users/activity', () =>
      HttpResponse.json([
        {
          at: '2026-01-01T12:00:00Z',
          event: 'login.failure',
          actor: 'nobody',
          subject: null,
          detail: 'unknown user',
          client: '10.0.0.5',
        },
      ]),
    ),
  ),
);

describe('UsersAuthPage', () => {
  it('lists the users with their role and state, and does not let one act on oneself', async () => {
    renderWithProviders(<UsersAuthPage />);

    const table = await screen.findByRole('table', { name: 'Users' });

    expect(within(table).getByText('Olivia')).toBeInTheDocument();
    expect(within(table).getByText('Disabled')).toBeInTheDocument();
    await vi.waitFor(() => expect(within(table).getByRole('button', { name: 'Disable user admin' })).toBeDisabled());
    expect(within(table).getByRole('button', { name: 'Delete user admin' })).toBeDisabled();
    expect(within(table).getByRole('button', { name: 'Enable user olivia' })).toBeEnabled();
  });

  it('creates a user with a role', async () => {
    const u = userEvent.setup();
    let body: unknown;
    server.use(
      http.post('/api/v1/users', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(user({ id: 3 }), { status: 201 });
      }),
    );
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('button', { name: 'Add user' }));
    await u.type(screen.getByLabelText('User name'), 'newcomer');
    await u.selectOptions(screen.getByLabelText('Role'), 'Viewer');
    await u.type(screen.getByLabelText('Password'), 'a long enough passphrase');
    await u.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() =>
      expect(body).toEqual({ username: 'newcomer', password: 'a long enough passphrase', roleId: 5 }),
    );
  });

  it('disables an enabled user and enables a disabled one', async () => {
    const u = userEvent.setup();
    const bodies: unknown[] = [];
    server.use(
      http.get('/api/v1/users', () =>
        HttpResponse.json([...users, user({ id: 3, username: 'grace', displayName: 'Grace' })]),
      ),
      http.put('/api/v1/users/:id', async ({ request, params }) => {
        bodies.push([params.id, await request.json()]);
        return HttpResponse.json(user({}));
      }),
    );
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('button', { name: 'Disable user grace' }));
    await u.click(screen.getByRole('button', { name: 'Enable user olivia' }));

    await vi.waitFor(() =>
      expect(bodies).toEqual([
        ['3', { disabled: true }],
        ['2', { disabled: false }],
      ]),
    );
  });

  it('asks to confirm before deleting a user', async () => {
    const u = userEvent.setup();
    let deleted = 0;
    server.use(
      http.delete('/api/v1/users/2', () => {
        deleted++;
        return HttpResponse.json(user({ id: 2 }));
      }),
    );
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('button', { name: 'Delete user olivia' }));
    expect(deleted).toBe(0);
    await u.click(screen.getByRole('button', { name: 'Confirm delete' }));

    await vi.waitFor(() => expect(deleted).toBe(1));
  });

  it("shows the server's reason when a change is refused", async () => {
    const u = userEvent.setup();
    server.use(
      http.put('/api/v1/users/2', () =>
        HttpResponse.json({ title: 'Conflict', detail: 'At least one enabled Admin must remain.' }, { status: 409 }),
      ),
    );
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('button', { name: 'Enable user olivia' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('At least one enabled Admin must remain.');
  });

  it('shows the roles; a built-in one is read only and can be duplicated', async () => {
    const u = userEvent.setup();
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('tab', { name: /Roles/ }));
    await u.click(screen.getByRole('button', { name: 'View role Admin' }));

    const form = screen.getByRole('group', { name: 'Apps' });
    expect(within(form).getByRole('checkbox')).toBeChecked();
    expect(within(form).getByRole('checkbox')).toBeDisabled();
    expect(screen.getByLabelText('Name')).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /secrets\.view/ })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Delete role Admin' })).not.toBeInTheDocument();
  });

  it('edits the permissions of a custom role, grouped by area', async () => {
    const u = userEvent.setup();
    let body: unknown;
    server.use(
      http.put('/api/v1/roles/5', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({});
      }),
    );
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('tab', { name: /Roles/ }));
    await u.click(screen.getByRole('button', { name: 'Edit role Viewer' }));
    expect(screen.getByRole('checkbox', { name: /Manage users and roles/ })).toBeDisabled();
    await u.click(screen.getByRole('checkbox', { name: /See maintenance/ }));
    await u.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() =>
      expect(body).toEqual({
        name: 'Viewer',
        description: 'Apps and data',
        permissions: ['apps.view', 'data.view', 'maintenance.view'],
      }),
    );
  });

  it('shows the latest events of the accounts', async () => {
    const u = userEvent.setup();
    renderWithProviders(<UsersAuthPage />);

    await u.click(await screen.findByRole('tab', { name: 'Activity' }));

    const table = await screen.findByRole('table', { name: 'Activity' });
    expect(within(table).getByText('login.failure')).toBeInTheDocument();
    expect(within(table).getByText('unknown user')).toBeInTheDocument();
  });
});
