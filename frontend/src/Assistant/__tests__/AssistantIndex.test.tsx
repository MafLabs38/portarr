import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { assistantStatus, server } from '__mocks__/server';
import AssistantIndex from 'Assistant/AssistantIndex';

describe('AssistantIndex', () => {
  it('shows the MCP server, its tools and how to connect Claude Code, without the key', async () => {
    renderWithProviders(<AssistantIndex />);

    const card = await screen.findByRole('region', { name: 'MCP server' });
    expect(within(card).getByText('On')).toBeInTheDocument();
    expect(within(card).getByText(`${window.location.origin}/mcp`)).toBeInTheDocument();
    const tools = within(card).getByRole('list', { name: 'MCP tools' });
    expect(within(tools).getByText('get_overview')).toBeInTheDocument();

    const command = screen.getByLabelText('Claude Code command');
    expect(command).toHaveTextContent(
      `claude mcp add --transport http portarr ${window.location.origin}/mcp --header "X-Api-Key: <your key>"`,
    );
  });

  it('renders the knowledge base with collapsible sections, VMs nested under their host', async () => {
    const user = userEvent.setup();
    renderWithProviders(<AssistantIndex />);

    const environments = await screen.findByText('Environments and machines');
    const section = environments.closest('details') as HTMLElement;
    expect(section).toHaveAttribute('open');
    const host = within(section).getByText('server-1', { selector: 'strong' }).closest('li') as HTMLElement;
    expect(within(host).getByText('vm-1', { selector: 'strong' })).toBeInTheDocument();

    const apps = screen.getByText('Stacks and apps');
    await user.click(apps);
    expect(screen.getByRole('columnheader', { name: 'App' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'https://sonarr.homelab.example/' })).toHaveAttribute('rel', 'noreferrer');
  });

  it('explains how to turn the server on when it is off', async () => {
    server.use(http.get('/api/v1/assistant', () => HttpResponse.json({ ...assistantStatus, enabled: false })));
    renderWithProviders(<AssistantIndex />);

    expect(await screen.findByText(/The MCP server is off/)).toBeInTheDocument();
    expect(screen.getByText('Off')).toBeInTheDocument();
  });

  it('marks the write tools and lists the latest catalog edits', async () => {
    renderWithProviders(<AssistantIndex />);

    const tools = await screen.findByRole('list', { name: 'MCP tools' });
    const setNotes = within(tools).getByText('set_notes').closest('li') as HTMLElement;
    expect(within(setNotes).getByText('writes')).toBeInTheDocument();

    const edits = screen.getByRole('region', { name: 'Catalog edits' });
    expect(within(edits).getByText(/^On: every edit/)).toBeInTheDocument();
    expect(await within(edits).findByText('set-notes providers/dns-host')).toBeInTheDocument();
    expect(within(edits).getByText(/^through mcp,/)).toBeInTheDocument();
  });

  it('says how to turn catalog edits on when they are off', async () => {
    server.use(
      http.get('/api/v1/assistant', () =>
        HttpResponse.json({ ...assistantStatus, writeTools: false, catalogWritable: false }),
      ),
    );
    renderWithProviders(<AssistantIndex />);

    const edits = await screen.findByRole('region', { name: 'Catalog edits' });
    expect(within(edits).getByText(/PORTARR__CATALOG__WRITABLE=true/)).toBeInTheDocument();
    expect(within(edits).queryByRole('list')).not.toBeInTheDocument();
  });
});
