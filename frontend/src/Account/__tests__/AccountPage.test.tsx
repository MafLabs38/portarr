import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import AccountPage from 'Account/AccountPage';

function Routed() {
  return (
    <Routes>
      <Route element={<AccountPage tab="profile" />} path="/account/profile" />
      <Route element={<AccountPage tab="api-keys" />} path="/account/api-keys" />
    </Routes>
  );
}

describe('AccountPage', () => {
  it('shows the profile and the password on the first tab and the API keys on the second', async () => {
    const user = userEvent.setup();
    server.use(http.get('/api/v1/auth/keys', () => HttpResponse.json([])));
    renderWithProviders(<Routed />, '/account/profile');

    expect(await screen.findByRole('heading', { name: 'Password' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'API keys' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'API keys' }));

    expect(await screen.findByRole('table', { name: 'API keys' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Password' })).not.toBeInTheDocument();
  });
});
