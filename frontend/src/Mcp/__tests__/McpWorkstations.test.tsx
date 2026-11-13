import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import McpIndex from 'Mcp/McpIndex';
import McpWorkstationDetail from 'Mcp/McpWorkstationDetail';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router';

import renderWithProviders from '__mocks__/renderWithProviders';
import { mcpWorkstationPlan, server } from '__mocks__/server';

function renderPlan(id: string) {
  return renderWithProviders(
    <Routes>
      <Route element={<McpWorkstationDetail />} path="/mcp-servers/workstations/:id" />
    </Routes>,
    `/mcp-servers/workstations/${id}`,
  );
}

describe('MCP workstations tab', () => {
  it('shows which server is wanted on which machine, in which assistants, and which is not', async () => {
    renderWithProviders(<McpIndex />, '/mcp-servers?tab=workstations');

    const matrix = await screen.findByRole('table', { name: 'Servers wanted on each workstation' });
    const wacken = within(matrix).getByRole('row', { name: /wacken/ });
    const quimper = within(matrix).getByRole('row', { name: /quimper/ });

    expect(
      within(wacken)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['Wanted in claude-code', '—Not wanted', 'Wanted in claude-code']);
    expect(within(quimper).getAllByRole('cell')[0]).toHaveTextContent('Wanted in claude-code, cursor');
    expect(within(quimper).getByRole('link', { name: 'quimper' })).toHaveAttribute(
      'href',
      '/mcp-servers/workstations/quimper',
    );
    expect(within(quimper).getByText('Windows')).toBeInTheDocument();
  });

  it('switches between the servers and the workstations with the keyboard and keeps the tab in the URL', async () => {
    const user = userEvent.setup();
    renderWithProviders(<McpIndex />);

    (await screen.findByRole('tab', { name: /Servers/ })).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: /Workstations/ })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByRole('table', { name: 'Servers wanted on each workstation' })).toBeInTheDocument();
  });

  it('explains where to name the machines when there is none', async () => {
    server.use(http.get('/api/v1/mcp/workstations', () => HttpResponse.json([])));
    renderWithProviders(<McpIndex />, '/mcp-servers?tab=workstations');

    expect(await screen.findByText(/No workstation yet/)).toBeInTheDocument();
  });
});

describe('McpWorkstationDetail', () => {
  it('shows the machine, what is wanted on it and the variables it must provide', async () => {
    renderPlan('wacken');

    const definition = await screen.findByRole('region', { name: 'Definition' });
    expect(within(definition).getByText('Linux')).toBeInTheDocument();
    expect(within(definition).getByText('The dev VM.')).toBeInTheDocument();
    expect(within(definition).getByRole('link', { name: 'memory' })).toHaveAttribute('href', '/mcp-servers/memory');
    expect(await screen.findByText(/must provide these variables: MEMORY_MCP_KEY/)).toBeInTheDocument();
  });

  it('lists what to install, then what to remove if present, each with its command', async () => {
    renderPlan('wacken');

    const install = await screen.findByRole('region', { name: 'memory · Claude Code' });
    expect(within(install).getByLabelText(/Add memory to claude-code\./)).toHaveTextContent(
      'claude mcp add --scope user memory',
    );

    const removal = screen.getByRole('region', { name: 'search · Claude Code' });
    expect(within(removal).getByLabelText(/If present, remove search from claude-code\./)).toHaveTextContent(
      'claude mcp remove --scope user search',
    );
    expect(screen.getByText(/Servers of the catalog that are not wanted here/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'github · Cursor' })).not.toBeInTheDocument();
  });

  it('tells the operator which keys of the vault the machine needs, so that he grants them there (D-116)', async () => {
    renderPlan('wacken');

    expect(await screen.findByText(/to these keys in your vault .*: kv\/mcp\/memory\./)).toBeInTheDocument();
    expect(screen.getByText(/Portarr holds no secret and reads none/)).toBeInTheDocument();
  });

  it('asks again for the system chosen', async () => {
    const asked: (string | null)[] = [];
    server.use(
      http.get('/api/v1/mcp/workstations/:id', ({ params, request }) => {
        const os = new URL(request.url).searchParams.get('os');
        asked.push(os);
        return HttpResponse.json(mcpWorkstationPlan(String(params.id), os ?? undefined));
      }),
    );
    const user = userEvent.setup();
    renderPlan('quimper');

    await user.selectOptions(await screen.findByLabelText('Operating system of the machine'), 'linux');

    await waitFor(() => expect(asked).toContain('linux'));
  });

  it('says so when the workstation is not in the catalog', async () => {
    renderPlan('nobody');

    expect(await screen.findByText('This workstation is not in the catalog.')).toBeInTheDocument();
  });
});
