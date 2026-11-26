import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, portal, server } from '__mocks__/server';
import CatalogPage from 'Settings/Catalog/CatalogPage';

const apps = {
  version: 'v1',
  section: 'apps',
  items: [
    { id: 'jellyfin', name: 'Jellyfin', icon: 'jellyfin' },
    { id: 'wiki', name: 'Wiki' },
  ],
};

function arrange(options: { writable?: boolean; admin?: boolean } = {}) {
  const { writable = true, admin = true } = options;
  server.use(
    http.get('/api/v1/portal', () => HttpResponse.json({ ...portal, writable })),
    http.get('/api/v1/catalog/apps', () => HttpResponse.json(apps)),
    http.get('/api/v1/auth/me', () =>
      HttpResponse.json({ ...me, permissions: admin ? ['catalog.write', 'settings.manage'] : ['catalog.write'] }),
    ),
  );
}

describe('CatalogPage', () => {
  it('lists the items of a section and the sections as tabs', async () => {
    arrange();
    renderWithProviders(<CatalogPage />);

    expect(await screen.findByText('Jellyfin')).toBeInTheDocument();
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      'Apps',
      'Environments',
      'Stacks',
      'Data',
      'Links',
      'Providers',
      'MCP servers',
      'Workstations',
    ]);
  });

  it('edits the MCP servers like the rest and keeps the keys that say how to reach one in the JSON', async () => {
    arrange();
    server.use(
      http.get('/api/v1/catalog/mcpServers', () =>
        HttpResponse.json({
          version: 'v1',
          section: 'mcpServers',
          items: [{ id: 'mnemarr', name: 'mnemarr', transport: 'http', http: { url: 'https://mnemarr.example/mcp' } }],
        }),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<CatalogPage />);

    await user.click(await screen.findByRole('tab', { name: 'MCP servers' }));
    await user.click(await screen.findByRole('button', { name: 'Edit mnemarr' }));

    expect(await screen.findByLabelText('Transport')).toHaveValue('http');
    expect((screen.getByLabelText(/The whole item/) as HTMLTextAreaElement).value).toContain(
      'https://mnemarr.example/mcp',
    );
  });

  it('says the catalog is read-only and offers no edit when writes are off', async () => {
    arrange({ writable: false });
    renderWithProviders(<CatalogPage />);

    expect(await screen.findByText('Jellyfin')).toBeInTheDocument();
    expect(screen.getAllByText(/PORTARR__CATALOG__WRITABLE=true/).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Add an item' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit jellyfin' })).not.toBeInTheDocument();
  });

  it('adds an item from the guided fields, sending the catalog version it read', async () => {
    const user = userEvent.setup();
    arrange();
    let sent: unknown;
    server.use(
      http.put('/api/v1/catalog/apps/grafana', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['added'] });
      }),
    );
    renderWithProviders(<CatalogPage />);
    await screen.findByText('Jellyfin');

    await user.click(screen.getByRole('button', { name: 'Add an item' }));
    await user.type(screen.getByLabelText('Id'), 'grafana');
    await user.type(screen.getByLabelText('Name'), 'Grafana');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(sent).toEqual({ version: 'v1', item: { id: 'grafana', name: 'Grafana' } });
  });

  it('sends an explicit null for a key the draft no longer holds, so that the server removes it', async () => {
    const user = userEvent.setup();
    arrange();
    let sent: unknown;
    server.use(
      http.put('/api/v1/catalog/apps/jellyfin', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['icon: removed'] });
      }),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('button', { name: 'Edit jellyfin' }));

    await user.clear(screen.getByLabelText(/The whole item/));
    await user.click(screen.getByLabelText(/The whole item/));
    await user.paste('{"id":"jellyfin","name":"Jellyfin"}');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(sent).toEqual({ version: 'v1', item: { id: 'jellyfin', name: 'Jellyfin', icon: null } });
  });

  it('checks an item without saving it and shows the server errors by field path', async () => {
    const user = userEvent.setup();
    arrange();
    server.use(
      http.post('/api/v1/catalog/apps/wiki/validate', () =>
        HttpResponse.json(
          { errors: { 'apps[wiki].stack': ["stack 'nowhere' is not declared in stacks."] } },
          { status: 400 },
        ),
      ),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('button', { name: 'Edit wiki' }));

    await user.click(screen.getByRole('button', { name: 'Check' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(/apps\[wiki\]\.stack: stack 'nowhere' is not declared/)).toBeInTheDocument();
  });

  it('saves a change of how a host is reached at once, without an approval step', async () => {
    const user = userEvent.setup();
    arrange({ admin: false });
    server.use(
      http.put('/api/v1/catalog/apps/wiki', () =>
        HttpResponse.json({ status: 'applied', version: 'v2', changes: ['check: added'] }),
      ),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('button', { name: 'Edit wiki' }));

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/saved/i)).toBeInTheDocument();
    expect(screen.queryByText(/administrator/)).not.toBeInTheDocument();
  });

  it('keeps the JSON and the guided fields in step, and refuses text that is not an object', async () => {
    const user = userEvent.setup();
    arrange();
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('button', { name: 'Edit jellyfin' }));

    await user.clear(screen.getByLabelText('Name'));
    await user.type(screen.getByLabelText('Name'), 'Jelly');
    expect((screen.getByLabelText(/The whole item/) as HTMLTextAreaElement).value).toContain('"name": "Jelly"');

    await user.clear(screen.getByLabelText(/The whole item/));
    await user.click(screen.getByLabelText(/The whole item/));
    await user.paste('[1]');
    expect(screen.getByText('This must be a JSON object.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('removes an item and says so', async () => {
    const user = userEvent.setup();
    arrange({ admin: false });
    server.use(
      http.delete('/api/v1/catalog/apps/:id', () =>
        HttpResponse.json({ status: 'applied', version: 'v2', changes: ['removed'] }),
      ),
    );
    renderWithProviders(<CatalogPage />);

    await user.click(await screen.findByRole('button', { name: 'Remove jellyfin' }));
    await user.click(screen.getByRole('button', { name: 'Confirm delete' }));

    expect(await screen.findByText('jellyfin removed.')).toBeInTheDocument();
  });
});
