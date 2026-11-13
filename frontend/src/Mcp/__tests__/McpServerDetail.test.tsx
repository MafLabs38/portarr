import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import McpServerDetail from 'Mcp/McpServerDetail';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router';

import renderWithProviders from '__mocks__/renderWithProviders';
import { mcpDetail, server } from '__mocks__/server';

function renderPage(id: string) {
  return renderWithProviders(
    <Routes>
      <Route element={<McpServerDetail />} path="/mcp-servers/:id" />
    </Routes>,
    `/mcp-servers/${id}`,
  );
}

describe('McpServerDetail', () => {
  it('shows what the catalog says about the server and how it is doing', async () => {
    renderPage('memory');

    const definition = await screen.findByRole('region', { name: 'Definition' });
    expect(within(definition).getByText('Healthy')).toBeInTheDocument();
    expect(within(definition).getByText('https://memory.homelab.example/mcp')).toBeInTheDocument();
    expect(within(definition).getByText(/^X-Api-Key ← \$MEMORY_MCP_KEY/)).toBeInTheDocument();
    expect(within(definition).getByText('Long-term memory.')).toBeInTheDocument();
    expect(within(definition).getByText(/edited in the catalog file/)).toBeInTheDocument();
  });

  it('gives the command and the file entry of each client, with a copy button, and the variable to provide', async () => {
    renderPage('memory');

    const claude = await screen.findByRole('region', { name: 'Claude Code' });
    expect(within(claude).getByText('MEMORY_MCP_KEY')).toBeInTheDocument();
    expect(
      within(claude).getByLabelText('Claude Code: Add the server with Claude Code, at user scope.'),
    ).toHaveTextContent('claude mcp add --transport http --scope user memory https://memory.homelab.example/mcp');
    // The command, the file entry, and the line that reads the vault into the variable.
    expect(within(claude).getAllByRole('button', { name: /Copy/ })).toHaveLength(3);
  });

  it('says why a client cannot hold the server instead of leaving it out', async () => {
    renderPage('memory');

    const zed = await screen.findByRole('region', { name: 'Zed' });

    expect(within(zed).getByText(/has no 'project' scope/)).toBeInTheDocument();
    expect(within(zed).queryByRole('button', { name: /Copy/ })).not.toBeInTheDocument();
  });

  it('asks again for the operating system chosen, so that paths and commands are those of the machine', async () => {
    const asked: (string | null)[] = [];
    server.use(
      http.get('/api/v1/mcp/servers/:id', ({ params, request }) => {
        const os = new URL(request.url).searchParams.get('os');
        asked.push(os);
        return HttpResponse.json(mcpDetail(String(params.id), os ?? 'linux'));
      }),
    );
    const user = userEvent.setup();
    renderPage('memory');

    await user.selectOptions(await screen.findByLabelText('Operating system of the machine'), 'windows');

    await waitFor(() => expect(asked).toContain('windows'));
    expect(await screen.findByText(/%USERPROFILE%/)).toBeInTheDocument();
  });

  it('warns about a package that is not pinned to a version', async () => {
    renderPage('github');

    expect(await screen.findByText(/not pinned to a version/)).toBeInTheDocument();
    expect(screen.getByText('Not monitored')).toBeInTheDocument();
  });

  it('says which vault key sits behind a variable, never a value (D-116)', async () => {
    renderPage('memory');

    const definition = await screen.findByRole('region', { name: 'Definition' });
    expect(within(definition).getByText('X-Api-Key ← $MEMORY_MCP_KEY (vault key memory)')).toBeInTheDocument();
  });

  it('gives the command that reads the vault into the variable, and the keys the machine must be able to read', async () => {
    renderPage('memory');

    const claude = await screen.findByRole('region', { name: 'Claude Code' });
    expect(within(claude).getByText('kv/mcp/memory')).toBeInTheDocument();
    expect(
      within(claude).getByLabelText(/Claude Code: In your shell profile: read the secrets from the vault/),
    ).toHaveTextContent('export MEMORY_MCP_KEY="$(vault kv get -field=value kv/mcp/memory)"');
  });

  it('says so when the server is not in the catalog', async () => {
    renderPage('ghost');

    expect(await screen.findByText('This MCP server is not in the catalog.')).toBeInTheDocument();
  });
});
