import { screen, within } from '@testing-library/react';
import McpIndex from 'Mcp/McpIndex';
import { http, HttpResponse } from 'msw';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';

describe('McpIndex', () => {
  it('lists the servers with whether they are up, and since when for a dead one', async () => {
    renderWithProviders(<McpIndex />);

    const list = await screen.findByRole('list', { name: 'MCP servers' });
    const memory = within(list).getByRole('listitem', { name: 'Memory' });
    expect(within(memory).getByText('Healthy')).toBeInTheDocument();
    expect(within(memory).getByText('seen 2 min ago')).toBeInTheDocument();
    expect(within(memory).getByText('Remote')).toBeInTheDocument();
    expect(within(memory).getByText('https://memory.homelab.example/mcp')).toBeInTheDocument();

    const search = within(list).getByRole('listitem', { name: 'Search' });
    expect(within(search).getByText('Down for 12 days')).toBeInTheDocument();
    expect(within(search).getByText(/last healthy 12 days ago/)).toBeInTheDocument();
  });

  it('shows a local server as not checked, with its command, and not as a fault', async () => {
    renderWithProviders(<McpIndex />);

    const github = await screen.findByRole('listitem', { name: 'GitHub' });
    expect(within(github).getByText('Not monitored')).toBeInTheDocument();
    expect(within(github).getByText('Local')).toBeInTheDocument();
    expect(within(github).getByText('npx -y @example/github-mcp')).toBeInTheDocument();
  });

  it('opens the page of a server', async () => {
    renderWithProviders(<McpIndex />);

    const link = await screen.findByRole('link', { name: 'Memory' });

    expect(link).toHaveAttribute('href', '/mcp-servers/memory');
  });

  it('explains where to describe the servers when there is none', async () => {
    server.use(http.get('/api/v1/mcp/servers', () => HttpResponse.json([])));
    renderWithProviders(<McpIndex />);

    expect(await screen.findByText(/No MCP server yet/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'MCP servers' })).not.toBeInTheDocument();
  });

  it('says so when the servers cannot be loaded', async () => {
    server.use(http.get('/api/v1/mcp/servers', () => HttpResponse.json({}, { status: 500 })));
    renderWithProviders(<McpIndex />);

    expect(await screen.findByText('Unable to load the MCP servers.')).toBeInTheDocument();
  });
});
