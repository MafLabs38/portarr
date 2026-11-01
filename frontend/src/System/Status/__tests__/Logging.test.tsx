import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import Logging from 'System/Status/Logging';

describe('Logging', () => {
  it('shows the current log level', async () => {
    renderWithProviders(<Logging />);

    const select = await screen.findByRole('combobox', { name: 'Log level' });

    await waitFor(() => expect(select).toHaveValue('Information'));
  });

  it('saves the level as soon as it is changed', async () => {
    const user = userEvent.setup();
    let saved: unknown;
    server.use(
      http.put('/api/v1/system/logging', async ({ request }) => {
        saved = await request.json();
        return HttpResponse.json({ level: 'Debug' });
      }),
    );
    renderWithProviders(<Logging />);
    const select = await screen.findByRole('combobox', { name: 'Log level' });
    await waitFor(() => expect(select).toBeEnabled());

    await user.selectOptions(select, 'Debug');

    await waitFor(() => expect(select).toHaveValue('Debug'));
    expect(saved).toEqual({ level: 'Debug' });
  });

  it('shows an error when the level cannot be saved', async () => {
    const user = userEvent.setup();
    server.use(http.put('/api/v1/system/logging', () => new HttpResponse(null, { status: 400 })));
    renderWithProviders(<Logging />);
    const select = await screen.findByRole('combobox', { name: 'Log level' });
    await waitFor(() => expect(select).toBeEnabled());

    await user.selectOptions(select, 'Trace');

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to change the log level.');
  });
});
