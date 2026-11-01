import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, server } from '__mocks__/server';
import { initialsOf } from 'Auth/Avatar';
import UserMenu from 'Auth/UserMenu';

describe('UserMenu', () => {
  it('shows a round button with the initials when there is no picture', async () => {
    renderWithProviders(<UserMenu />);

    const button = await screen.findByRole('button', { name: 'Account menu of Ada Admin' });

    expect(button).toHaveTextContent('AA');
    expect(button).toHaveAttribute('aria-expanded', 'false');
  });

  it('opens a menu with who is signed in, Profile, API keys and Log out', async () => {
    const user = userEvent.setup();
    renderWithProviders(<UserMenu />);

    await user.click(await screen.findByRole('button', { name: 'Account menu of Ada Admin' }));

    const menu = screen.getByRole('menu');
    expect(within(menu).getByText('Ada Admin')).toBeInTheDocument();
    expect(within(menu).getByText('admin')).toBeInTheDocument();
    expect(within(menu).getByText('Role: Admin')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Profile' })).toHaveAttribute('href', '/account/profile');
    expect(within(menu).getByRole('menuitem', { name: 'API keys' })).toHaveAttribute('href', '/account/api-keys');
    expect(within(menu).getByRole('menuitem', { name: 'Log out' })).toBeInTheDocument();
  });

  it('closes on Escape', async () => {
    const user = userEvent.setup();
    renderWithProviders(<UserMenu />);
    await user.click(await screen.findByRole('button', { name: 'Account menu of Ada Admin' }));

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('logs out with the CSRF header', async () => {
    const user = userEvent.setup();
    let header: string | null = null;
    server.use(
      http.post('/api/v1/auth/logout', ({ request }) => {
        header = request.headers.get('X-Requested-With');
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderWithProviders(<UserMenu />);
    await user.click(await screen.findByRole('button', { name: 'Account menu of Ada Admin' }));

    await user.click(screen.getByRole('menuitem', { name: 'Log out' }));

    await vi.waitFor(() => expect(header).toBe('portarr'));
  });

  it('shows the uploaded picture clipped in the circle', async () => {
    server.use(
      http.get('/api/v1/auth/me', () =>
        HttpResponse.json({ ...me, avatarUrl: '/api/v1/avatars/1?v=0123456789abcdef' }),
      ),
    );
    const { container } = renderWithProviders(<UserMenu />);

    await screen.findByRole('button', { name: 'Account menu of Ada Admin' });

    expect(container.querySelector('img')).toHaveAttribute('src', '/api/v1/avatars/1?v=0123456789abcdef');
  });
});

describe('Avatar', () => {
  it.each([
    ['Ada Admin', 'admin', 'AA'],
    ['Grace Brewster Hopper', 'grace', 'GH'],
    ['', 'operator-user', 'OP'],
    ['Plato', 'plato', 'PL'],
  ])('derives the initials of "%s" (%s)', (displayName, username, initials) => {
    expect(initialsOf(displayName, username)).toBe(initials);
  });
});
