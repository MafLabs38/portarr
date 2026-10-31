import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, server } from '__mocks__/server';
import ProfileCard from 'Account/ProfileCard';

describe('ProfileCard', () => {
  it('shows who is signed in, with the role', async () => {
    renderWithProviders(<ProfileCard />);

    expect(await screen.findByText('Ada Admin', { selector: 'span' })).toBeInTheDocument();
    expect(screen.getByText('admin')).toBeInTheDocument();
    expect(screen.getByText('Role: Admin')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove picture' })).not.toBeInTheDocument();
  });

  it('saves a changed display name, trimmed', async () => {
    const user = userEvent.setup();
    let body: unknown;
    server.use(
      http.put('/api/v1/auth/profile', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...me, displayName: 'Root' });
      }),
    );
    renderWithProviders(<ProfileCard />);

    const save = await screen.findByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    await user.clear(screen.getByLabelText('Display name'));
    await user.type(screen.getByLabelText('Display name'), '  Root ');
    await user.click(save);

    expect(await screen.findByRole('status')).toHaveTextContent('Saved.');
    expect(body).toEqual({ displayName: 'Root' });
  });

  it('says when the profile could not be saved', async () => {
    const user = userEvent.setup();
    server.use(http.put('/api/v1/auth/profile', () => HttpResponse.json({}, { status: 500 })));
    renderWithProviders(<ProfileCard />);

    await user.type(await screen.findByLabelText('Display name'), '!');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to save the profile.');
  });

  it('uploads a picture, and offers to remove one that exists', async () => {
    const user = userEvent.setup();
    let received: FormDataEntryValue | null = null;
    server.use(
      http.get('/api/v1/auth/me', () => HttpResponse.json({ ...me, avatarUrl: '/api/v1/avatars/1?v=a' })),
      http.put('/api/v1/auth/avatar', async ({ request }) => {
        received = (await request.formData()).get('file');
        return HttpResponse.json(me);
      }),
    );
    renderWithProviders(<ProfileCard />);

    expect(await screen.findByRole('button', { name: 'Remove picture' })).toBeInTheDocument();
    await user.upload(screen.getByLabelText('Avatar image'), new File(['png'], 'me.png', { type: 'image/png' }));

    await vi.waitFor(() => expect((received as File | null)?.size).toBe(3));
  });

  it('refuses an image the server rejects with a hint about the accepted formats', async () => {
    const user = userEvent.setup();
    server.use(http.put('/api/v1/auth/avatar', () => HttpResponse.json({}, { status: 400 })));
    renderWithProviders(<ProfileCard />);

    await user.upload(await screen.findByLabelText('Avatar image'), new File(['x'], 'me.png', { type: 'image/png' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'That is not a PNG, WebP or JPEG image of 1 MiB at most.',
    );
  });
});
