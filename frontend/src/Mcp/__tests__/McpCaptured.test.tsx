import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import McpIndex from 'Mcp/McpIndex';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { me, server } from '__mocks__/server';

const open = () => renderWithProviders(<McpIndex />, '/mcp-servers?tab=captured');

function recordDecision() {
  const bodies: { path: string; body: unknown }[] = [];
  server.use(
    http.post('/api/v1/mcp/captures/:key/accept', async ({ request, params }) => {
      bodies.push({ path: `${String(params.key)}/accept`, body: await request.json() });
      return HttpResponse.json({
        written: true,
        readOnly: false,
        serverId: 'search',
        workstationsUpdated: ['quimper'],
        skipped: ['laptop: not a workstation of the catalog (declare it first).'],
        errors: [],
        yaml: null,
      });
    }),
    http.post('/api/v1/mcp/captures/:key/dismiss', ({ params }) => {
      bodies.push({ path: `${String(params.key)}/dismiss`, body: null });
      return HttpResponse.json({ dismissed: 1 });
    }),
  );
  return bodies;
}

describe('MCP captured tab', () => {
  it('lists what the assistants handed over, where it was seen and how it compares with the catalog', async () => {
    open();

    const list = await screen.findByRole('list', { name: 'Captured' });
    const search = within(list).getByRole('listitem', { name: 'Search Tool' });
    expect(within(search).getByText('New')).toBeInTheDocument();
    expect(within(search).getByText(/Seen on quimper · claude-code \(user scope\) 5 min ago/)).toBeInTheDocument();
    expect(
      within(search).getByText(/Seen on laptop · cursor .* not a workstation of the catalog yet/),
    ).toBeInTheDocument();

    const memory = within(list).getByRole('listitem', { name: 'memory' });
    expect(within(memory).getByText('Differs from memory')).toBeInTheDocument();
    expect(within(memory).getByText('header X-Api-Key: variable MEMORY_MCP_KEY → OTHER_KEY')).toBeInTheDocument();
  });

  it('counts the waiting servers on the tab and explains how to capture when nothing waits', async () => {
    server.use(http.get('/api/v1/mcp/captures', () => HttpResponse.json([])));
    open();

    expect(await screen.findByText(/Nothing is waiting/)).toBeInTheDocument();
    expect(screen.getByText(/capture my MCP configuration into Portarr/)).toBeInTheDocument();
  });

  it('adds a new server with the variable the person names, and says what was written and what was not', async () => {
    const bodies = recordDecision();
    const user = userEvent.setup();
    open();

    const search = await screen.findByRole('listitem', { name: 'Search Tool' });
    await user.click(await within(search).findByRole('button', { name: 'Add to the catalog…' }));
    const form = within(search).getByRole('form', { name: 'Add to the catalog' });
    expect(within(form).getByLabelText('Id in the catalog')).toHaveValue('search-tool');
    const variable = within(form).getByLabelText('header X-Api-Key: variable that holds it');
    expect(variable).toHaveValue('SEARCH_TOOL_X_API_KEY');
    await user.clear(variable);
    await user.type(variable, 'SEARCH_KEY');
    expect(within(form).getByLabelText('Also add it to the wanted list of quimper')).toBeChecked();
    await user.click(within(form).getByRole('button', { name: 'Add to the catalog' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      path: 'aaaaaaaaaaaa/accept',
      body: {
        mode: 'new',
        id: 'search-tool',
        name: 'Search Tool',
        variables: { 'header:X-Api-Key': 'SEARCH_KEY' },
        addToWorkstations: true,
      },
    });
    expect(await screen.findByText('Written to the catalog as search.')).toBeInTheDocument();
    expect(screen.getByText('Wanted list updated: quimper.')).toBeInTheDocument();
    expect(screen.getByText(/Not updated: laptop: not a workstation of the catalog/)).toBeInTheDocument();
  });

  it('updates the definition it differs from, without asking for an id', async () => {
    const bodies = recordDecision();
    const user = userEvent.setup();
    open();

    const memory = await screen.findByRole('listitem', { name: 'memory' });
    await user.click(await within(memory).findByRole('button', { name: 'Update memory…' }));
    const form = within(memory).getByRole('form', { name: 'Update the definition' });
    expect(within(form).queryByLabelText('Id in the catalog')).not.toBeInTheDocument();
    await user.click(within(form).getByRole('button', { name: 'Update the definition' }));

    await waitFor(() => expect(bodies[0]?.path).toBe('bbbbbbbbbbbb/accept'));
    expect((bodies[0]?.body as { mode: string }).mode).toBe('replace');
  });

  it('keeps the catalogs version and only updates the wanted lists', async () => {
    const bodies = recordDecision();
    const user = userEvent.setup();
    open();

    await user.click(await screen.findByRole('button', { name: "Keep the catalog's version" }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]?.body).toEqual({ mode: 'existing', addToWorkstations: true });
  });

  it('gives the yaml to paste when the catalog is read-only', async () => {
    server.use(
      http.post('/api/v1/mcp/captures/:key/accept', () =>
        HttpResponse.json({
          written: false,
          readOnly: true,
          serverId: 'search',
          workstationsUpdated: [],
          skipped: [],
          errors: ['The catalog is read-only: set PORTARR__CATALOG__WRITABLE=true to edit it through Portarr.'],
          yaml: 'mcpServers:\n  - id: search',
        }),
      ),
    );
    const user = userEvent.setup();
    open();

    const search = await screen.findByRole('listitem', { name: 'Search Tool' });
    await user.click(await within(search).findByRole('button', { name: 'Add to the catalog…' }));
    await user.click(within(search).getByRole('button', { name: 'Add to the catalog' }));

    expect(
      await screen.findByText('The catalog is read-only here. Paste this into the catalog file:'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('The catalog is read-only here. Paste this into the catalog file:')).toHaveTextContent(
      'id: search',
    );
  });

  it('shows why the catalog refused a definition', async () => {
    server.use(
      http.post('/api/v1/mcp/captures/:key/accept', () =>
        HttpResponse.json({ detail: "The catalog already has a server 'memory'." }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    open();

    const search = await screen.findByRole('listitem', { name: 'Search Tool' });
    await user.click(await within(search).findByRole('button', { name: 'Add to the catalog…' }));
    await user.click(within(search).getByRole('button', { name: 'Add to the catalog' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("Refused: The catalog already has a server 'memory'.");
  });

  it('dismisses a server without writing anything', async () => {
    const bodies = recordDecision();
    const user = userEvent.setup();
    open();

    const search = await screen.findByRole('listitem', { name: 'Search Tool' });
    await user.click(within(search).getByRole('button', { name: 'Dismiss' }));

    await waitFor(() => expect(bodies).toEqual([{ path: 'aaaaaaaaaaaa/dismiss', body: null }]));
  });

  it('offers only to dismiss to a role that may not write the catalog, and says why', async () => {
    server.use(
      http.get('/api/v1/auth/me', () =>
        HttpResponse.json({ ...me, role: 'Reader', permissions: me.permissions.filter((p) => p !== 'catalog.write') }),
      ),
    );
    open();

    const search = await screen.findByRole('listitem', { name: 'Search Tool' });

    expect(await within(search).findByText(/needs a role that may edit it/)).toBeInTheDocument();
    expect(within(search).queryByRole('button', { name: 'Add to the catalog…' })).not.toBeInTheDocument();
    expect(within(search).getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
  });
});
