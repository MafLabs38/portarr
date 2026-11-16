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

const pendingChange = {
  id: 'abc123',
  at: '2026-11-16T07:03:00Z',
  channel: 'mcp',
  user: null,
  via: 'chat:42',
  section: 'environments',
  itemId: 'server-2',
  operation: 'upsert',
  before: null,
  after: { id: 'server-2', name: 'Server 2', docker: { url: 'http://10.0.0.30:2375' } },
  changes: ['added'],
};

function arrange(options: { writable?: boolean; admin?: boolean; pending?: unknown[] } = {}) {
  const { writable = true, admin = true, pending = [] } = options;
  server.use(
    http.get('/api/v1/portal', () => HttpResponse.json({ ...portal, writable })),
    http.get('/api/v1/catalog/apps', () => HttpResponse.json(apps)),
    http.get('/api/v1/catalog/pending', () => HttpResponse.json(pending)),
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
      'Pending0',
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

  it('says a change of how a host is reached waits for an administrator', async () => {
    const user = userEvent.setup();
    arrange({ admin: false });
    server.use(
      http.put('/api/v1/catalog/apps/wiki', () =>
        HttpResponse.json(
          { status: 'pending', version: 'v1', changes: ['check: added'], pendingId: 'p1' },
          { status: 202 },
        ),
      ),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('button', { name: 'Edit wiki' }));

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText(/waits for an administrator/)).toBeInTheDocument();
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

  it('says a removal waits for an administrator when the server keeps it for approval', async () => {
    const user = userEvent.setup();
    arrange({ admin: false });
    server.use(
      http.delete('/api/v1/catalog/apps/:id', () =>
        HttpResponse.json(
          { status: 'pending', version: 'v1', changes: ['removed'], pendingId: 'abc123' },
          { status: 202 },
        ),
      ),
    );
    renderWithProviders(<CatalogPage />);

    await user.click(await screen.findByRole('button', { name: 'Remove jellyfin' }));
    await user.click(screen.getByRole('button', { name: 'Confirm delete' }));

    expect(await screen.findByText(/waits for an administrator/)).toBeInTheDocument();
    expect(screen.queryByText('jellyfin removed.')).not.toBeInTheDocument();
  });

  it('lets an administrator approve a waiting change and shows what would change', async () => {
    const user = userEvent.setup();
    arrange({ pending: [pendingChange] });
    let approved: string | undefined;
    server.use(
      http.post('/api/v1/catalog/pending/:id/approve', ({ params }) => {
        approved = String(params.id);
        return HttpResponse.json({ status: 'applied', version: 'v2', changes: ['added'] });
      }),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('tab', { name: /Pending/ }));

    expect(await screen.findByText('environments / server-2')).toBeInTheDocument();
    expect(screen.getByLabelText('After')).toHaveTextContent('http://10.0.0.30:2375');

    await user.click(screen.getByRole('button', { name: 'Approve' }));

    expect(approved).toBe('abc123');
  });

  it('lets an administrator reject a waiting change', async () => {
    const user = userEvent.setup();
    arrange({ pending: [pendingChange] });
    let rejected: string | undefined;
    server.use(
      http.delete('/api/v1/catalog/pending/:id', ({ params }) => {
        rejected = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('tab', { name: /Pending/ }));

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    expect(rejected).toBe('abc123');
  });

  it('tells the administrator why an approval was refused and keeps the change', async () => {
    const user = userEvent.setup();
    arrange({ pending: [pendingChange] });
    server.use(
      http.post('/api/v1/catalog/pending/:id/approve', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            detail: "environments 'server-2', docker, changed since this was asked for: reject it and ask again.",
          },
          { status: 409 },
        ),
      ),
    );
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('tab', { name: /Pending/ }));

    await user.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(await screen.findByText(/changed since this was asked for/)).toBeInTheDocument();
    expect(screen.getByText('environments / server-2')).toBeInTheDocument();
  });

  it('shows a waiting change but no decision buttons to someone who is not an administrator', async () => {
    const user = userEvent.setup();
    arrange({ admin: false, pending: [pendingChange] });
    renderWithProviders(<CatalogPage />);
    await user.click(await screen.findByRole('tab', { name: /Pending/ }));

    expect(await screen.findByText('environments / server-2')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.getByText('An administrator approves these changes.')).toBeInTheDocument();
  });
});
