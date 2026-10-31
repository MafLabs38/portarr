import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { authStatus, me, server } from '__mocks__/server';
import { createQueryClient } from 'App/queryClient';
import AuthGate from 'Auth/AuthGate';

const unauthorized = () => http.get('/api/v1/auth/me', () => new HttpResponse(null, { status: 401 }));

describe('AuthGate', () => {
  it('shows the app to a signed-in user', async () => {
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    expect(await screen.findByText('The app')).toBeInTheDocument();
  });

  it('shows the login page, and nothing of the app, without a session', async () => {
    server.use(unauthorized());
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Homelab' })).toBeInTheDocument();
    expect(screen.queryByText('The app')).not.toBeInTheDocument();
  });

  it('goes to the setup page while no user exists', async () => {
    server.use(
      unauthorized(),
      http.get('/api/v1/auth/status', () => HttpResponse.json({ ...authStatus, setupRequired: true })),
    );
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    expect(await screen.findByRole('button', { name: 'Create the administrator' })).toBeDisabled();
  });

  it('says so when Portarr cannot be reached, instead of asking for a password', async () => {
    server.use(http.get('/api/v1/auth/me', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to reach Portarr');
  });
});

describe('LoginPage', () => {
  it('signs in with the user name, the password and the CSRF header, then shows the app', async () => {
    const user = userEvent.setup();
    let signedIn = false;
    let sent: { header: string | null; body: unknown } | undefined;
    server.use(
      http.get('/api/v1/auth/me', () => (signedIn ? HttpResponse.json(me) : new HttpResponse(null, { status: 401 }))),
      http.post('/api/v1/auth/login', async ({ request }) => {
        sent = { header: request.headers.get('X-Requested-With'), body: await request.json() };
        signedIn = true;
        return HttpResponse.json(me);
      }),
    );
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    await user.type(await screen.findByLabelText('User name'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByLabelText('Keep me signed in on this device'));
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('The app')).toBeInTheDocument();
    expect(sent).toEqual({
      header: 'portarr',
      body: { username: 'admin', password: 'correct horse battery', remember: true },
    });
  });

  it('gives one generic message when the sign-in fails', async () => {
    const user = userEvent.setup();
    server.use(
      unauthorized(),
      http.post('/api/v1/auth/login', () => new HttpResponse(null, { status: 401 })),
    );
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );

    await user.type(await screen.findByLabelText('User name'), 'someone');
    await user.type(screen.getByLabelText('Password'), 'not the password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Invalid username or password, or the account is temporarily locked.',
      ),
    );
  });
});

describe("AuthGate with the app's own query client", () => {
  it('keeps the login form and its message when the credentials are refused', async () => {
    const user = userEvent.setup();
    let meCalls = 0;
    server.use(
      http.get('/api/v1/auth/me', () => {
        meCalls++;
        return new HttpResponse(null, { status: 401 });
      }),
      http.post('/api/v1/auth/login', () => new HttpResponse(null, { status: 401 })),
    );
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
      '/',
      createQueryClient(),
    );

    await user.type(await screen.findByLabelText('User name'), 'someone');
    await user.type(screen.getByLabelText('Password'), 'not the password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid username or password');
    // The refusal is not an ended session: nobody asks again who is signed in, the form is still there.
    expect(meCalls).toBe(1);
    expect(screen.getByLabelText('User name')).toHaveValue('someone');
  });
});

describe('SetupPage', () => {
  it('creates the first administrator once the passwords match and are long enough', async () => {
    const user = userEvent.setup();
    let body: unknown;
    server.use(
      unauthorized(),
      http.get('/api/v1/auth/status', () => HttpResponse.json({ ...authStatus, setupRequired: true })),
      http.post('/api/v1/auth/setup', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(me, { status: 201 });
      }),
    );
    renderWithProviders(
      <AuthGate>
        <p>The app</p>
      </AuthGate>,
    );
    const create = await screen.findByRole('button', { name: 'Create the administrator' });

    await user.type(screen.getByLabelText('User name'), 'root');
    await user.type(screen.getByLabelText('Password'), 'too short');
    await user.type(screen.getByLabelText('Confirm the password'), 'too short');
    expect(create).toBeDisabled();
    expect(screen.getByText('At least 10 characters are needed.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Password'));
    await user.clear(screen.getByLabelText('Confirm the password'));
    await user.type(screen.getByLabelText('Password'), 'a long enough passphrase');
    await user.type(screen.getByLabelText('Confirm the password'), 'a long enough passphrase');
    await user.click(create);

    await waitFor(() => expect(body).toEqual({ username: 'root', password: 'a long enough passphrase' }));
  });
});
