import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import PasswordCard from 'Account/PasswordCard';

async function fill(user: ReturnType<typeof userEvent.setup>, current: string, next: string, confirmation: string) {
  await user.type(screen.getByLabelText('Current password'), current);
  await user.type(screen.getByLabelText('New password'), next);
  await user.type(screen.getByLabelText('Confirm the password'), confirmation);
}

describe('PasswordCard', () => {
  it('stays disabled until the new password is long enough and confirmed', async () => {
    const user = userEvent.setup();
    renderWithProviders(<PasswordCard />);
    const save = screen.getByRole('button', { name: 'Change password' });

    await fill(user, 'old password', 'short', 'short');
    expect(save).toBeDisabled();

    await user.clear(screen.getByLabelText('New password'));
    await user.clear(screen.getByLabelText('Confirm the password'));
    await user.type(screen.getByLabelText('New password'), 'a long enough password');
    await user.type(screen.getByLabelText('Confirm the password'), 'another long password');
    expect(save).toBeDisabled();

    await user.clear(screen.getByLabelText('Confirm the password'));
    await user.type(screen.getByLabelText('Confirm the password'), 'a long enough password');
    expect(save).toBeEnabled();
  });

  it('sends both passwords, then empties the form and says other sessions were signed out', async () => {
    const user = userEvent.setup();
    let body: unknown;
    server.use(
      http.put('/api/v1/auth/password', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderWithProviders(<PasswordCard />);

    await fill(user, 'old password', 'a long enough password', 'a long enough password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Password changed. Other sessions were signed out.');
    expect(body).toEqual({ currentPassword: 'old password', newPassword: 'a long enough password' });
    expect(screen.getByLabelText('Current password')).toHaveValue('');
    expect(screen.getByLabelText('New password')).toHaveValue('');
  });

  it.each([
    [403, 'The current password is not right.'],
    [400, 'That user name or password is not acceptable.'],
    [500, 'Unable to change the password.'],
  ])('explains a %i answer', async (status, message) => {
    const user = userEvent.setup();
    server.use(http.put('/api/v1/auth/password', () => HttpResponse.json({ detail: 'no' }, { status })));
    renderWithProviders(<PasswordCard />);

    await fill(user, 'old password', 'a long enough password', 'a long enough password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.getByLabelText('Current password')).toHaveValue('old password');
  });
});
