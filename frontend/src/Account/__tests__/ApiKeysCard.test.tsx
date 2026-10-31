import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import ApiKeysCard from 'Account/ApiKeysCard';
import type { ApiKey } from 'typings/Auth';

const key = (over: Partial<ApiKey>): ApiKey => ({
  id: 1,
  name: 'CI',
  prefix: 'ptr_AbCdEf',
  scope: 'read',
  created: '2026-01-01T12:00:00Z',
  lastUsed: null,
  expires: null,
  revoked: null,
  active: true,
  ...over,
});

describe('ApiKeysCard', () => {
  it('lists the keys by prefix, never the key', async () => {
    server.use(
      http.get('/api/v1/auth/keys', () =>
        HttpResponse.json([key({}), key({ id: 2, name: 'Old', revoked: '2026-01-02T00:00:00Z', active: false })]),
      ),
    );
    renderWithProviders(<ApiKeysCard />);

    const table = await screen.findByRole('table', { name: 'API keys' });

    expect(await within(table).findAllByText('ptr_AbCdEf…', { exact: false })).toHaveLength(2);
    expect(within(table).getByText('Revoked')).toBeInTheDocument();
    expect(within(table).getAllByRole('button', { name: /Revoke key/ })).toHaveLength(1);
  });

  it('shows a new key once, with its scope', async () => {
    const user = userEvent.setup();
    let body: unknown;
    server.use(
      http.get('/api/v1/auth/keys', () => HttpResponse.json([])),
      http.post('/api/v1/auth/keys', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          { key: key({ name: 'Scripts', scope: 'full' }), value: 'ptr_the-whole-secret-value' },
          { status: 201 },
        );
      }),
    );
    renderWithProviders(<ApiKeysCard />);

    await user.type(await screen.findByLabelText('Name'), 'Scripts');
    await user.selectOptions(screen.getByLabelText('Access'), 'full');
    await user.click(screen.getByRole('button', { name: 'Create key' }));

    expect(await screen.findByText('ptr_the-whole-secret-value')).toBeInTheDocument();
    expect(screen.getByText('Key "Scripts" created. Copy it now:')).toBeInTheDocument();
    expect(body).toEqual({ name: 'Scripts', scope: 'full' });
  });
});
