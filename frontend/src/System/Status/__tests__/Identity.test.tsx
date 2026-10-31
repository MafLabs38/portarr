import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { portal, server } from '__mocks__/server';
import Identity from 'System/Status/Identity';

const writable = () => server.use(http.get('/api/v1/portal', () => HttpResponse.json({ ...portal, writable: true })));

describe('Identity', () => {
  it('says how to turn catalog writes on when they are off', async () => {
    renderWithProviders(<Identity />);

    expect(await screen.findByText(/PORTARR__CATALOG__WRITABLE=true/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Name')).not.toBeInTheDocument();
  });

  it('sends the new name with the API key, and nothing else in the body', async () => {
    const user = userEvent.setup();
    writable();
    let sent: { key: string | null; body: unknown } | undefined;
    server.use(
      http.put('/api/v1/portal', async ({ request }) => {
        sent = { key: request.headers.get('X-Api-Key'), body: await request.json() };
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['name: changed'] });
      }),
    );
    renderWithProviders(<Identity />);
    const name = await screen.findByLabelText('Name');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    await user.clear(name);
    await user.type(name, 'Lab');
    await user.type(screen.getByLabelText('API key'), 'k'.repeat(32));
    await user.click(save);

    expect(await screen.findByRole('status')).toHaveTextContent('Saved.');
    expect(sent).toEqual({ key: 'k'.repeat(32), body: { name: 'Lab' } });
  });

  it('sends the colors alone, with the key, when only a color changed', async () => {
    const user = userEvent.setup();
    writable();
    let body: unknown;
    server.use(
      http.put('/api/v1/portal', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['colors.name: changed'] });
      }),
    );
    renderWithProviders(<Identity />);
    const color = await screen.findByLabelText('Name color');
    expect(color).toHaveValue('accent');
    expect(screen.getByLabelText('Version color')).toHaveValue('accent');

    await user.clear(color);
    await user.type(color, '#ffc145');
    await user.type(screen.getByLabelText('API key'), 'k'.repeat(32));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Saved.');
    expect(body).toEqual({ colors: { name: '#ffc145', version: 'accent' } });
  });

  it('uploads the chosen logo as multipart with the API key', async () => {
    const user = userEvent.setup();
    writable();
    let sent: { key: string | null; type: string | null; file: FormDataEntryValue | null } | undefined;
    server.use(
      http.put('/api/v1/portal/logo', async ({ request }) => {
        sent = {
          key: request.headers.get('X-Api-Key'),
          type: request.headers.get('Content-Type'),
          file: (await request.formData()).get('file'),
        };
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['logo: changed'] });
      }),
    );
    renderWithProviders(<Identity />);
    await screen.findByLabelText('Name');

    await user.upload(
      screen.getByLabelText('Logo'),
      new File([new Uint8Array([137, 80, 78, 71])], 'avatar.png', { type: 'image/png' }),
    );
    await user.type(screen.getByLabelText('API key'), 'k'.repeat(32));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Saved.');
    expect(sent?.key).toBe('k'.repeat(32));
    expect(sent?.type).toMatch(/^multipart\/form-data; boundary=/);
    expect((sent?.file as Blob).size).toBe(4);
  });

  it.each([
    [401, 'The API key is missing or wrong.'],
    [400, /was refused/],
  ])('explains a %i answer', async (status, message) => {
    const user = userEvent.setup();
    writable();
    server.use(http.put('/api/v1/portal', () => new HttpResponse(null, { status })));
    renderWithProviders(<Identity />);
    const name = await screen.findByLabelText('Name');

    await user.type(name, '!');
    await user.type(screen.getByLabelText('API key'), 'k'.repeat(32));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(message));
  });

  it('says why a declared logo is not used', async () => {
    server.use(
      http.get('/api/v1/portal', () =>
        HttpResponse.json({ ...portal, logoProblem: 'The file is not a valid png image.' }),
      ),
    );
    renderWithProviders(<Identity />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The logo is not used: The file is not a valid png image.',
    );
  });
});
