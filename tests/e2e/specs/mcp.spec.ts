import { expect, test } from '@playwright/test';

// V9: the operator's MCP servers, the workstations, and the inbox of captured servers. The stack's catalog declares three servers (a stub
// that answers like an MCP server, a port nothing listens on, a local process) and two workstations. No assistant is ever called.
test.describe('MCP', { tag: '@smoke' }, () => {
  test('lists the servers: one up, one down since a moment, a local one that is not monitored', async ({ page }) => {
    await page.goto('/mcp-servers');
    await expect(page.getByRole('heading', { level: 1, name: 'MCP servers' })).toBeVisible();

    const list = page.getByRole('list', { name: 'MCP servers' });
    // The first check runs a few seconds after the start: the state is a word, never a color alone.
    await expect(list.getByRole('listitem', { name: 'Memory' }).getByText('Healthy', { exact: true })).toBeVisible({
      timeout: 30_000,
    });
    const search = list.getByRole('listitem', { name: 'Search' });
    await expect(search.getByText(/^Down for /)).toBeVisible({ timeout: 30_000 });
    await expect(search.getByText(/never seen healthy/)).toBeVisible();
    const github = list.getByRole('listitem', { name: 'GitHub' });
    await expect(github.getByText('Not monitored')).toBeVisible();
    await expect(github.getByText('Local', { exact: true })).toBeVisible();
  });

  test('a server page gives the command and the file entry for each client, for the system chosen', async ({
    page,
  }) => {
    await page.goto('/mcp-servers');
    await page.getByRole('link', { name: 'Memory' }).click();

    await expect(page.getByRole('heading', { level: 1, name: 'Memory' })).toBeVisible();
    const claude = page.getByRole('region', { name: 'Claude Code' });
    await expect(claude).toBeVisible();
    await expect(claude.getByText('MEMORY_MCP_KEY').first()).toBeVisible();
    await expect(claude.locator('pre').first()).toContainText(
      'claude mcp add --transport http --scope user memory http://tools:8080/mcp',
    );
    // The variable is read by the client, never written: no secret value exists anywhere in the page.
    await expect(claude.locator('pre').first()).toContainText("'X-Api-Key: ${MEMORY_MCP_KEY}'");

    await page.getByLabel('Operating system of the machine').selectOption('windows');
    await expect(claude.getByText(/Merge this into %USERPROFILE%\\\.claude\.json/)).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'Cursor' }).getByText(/%USERPROFILE%\\\.cursor\\mcp\.json/),
    ).toBeVisible();
  });

  test('the workstations tab says what is wanted where, and a machine page gives its plan', async ({ page }) => {
    await page.goto('/mcp-servers?tab=workstations');

    const matrix = page.getByRole('table', { name: 'Servers wanted on each workstation' });
    await expect(matrix).toBeVisible();
    await expect(
      matrix.getByRole('row', { name: /Dev laptop/ }).getByText('Wanted in claude-code, cursor'),
    ).toBeVisible();
    await expect(
      matrix
        .getByRole('row', { name: /Dev VM/ })
        .getByText('Not wanted')
        .first(),
    ).toBeVisible();

    await matrix.getByRole('link', { name: 'Dev laptop' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Dev laptop' })).toBeVisible();
    await expect(page.getByText(/must provide these variables: GITHUB_TOKEN, MEMORY_MCP_KEY/)).toBeVisible();
    await expect(page.getByRole('region', { name: 'memory · Cursor' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'github · Claude Code' })).toBeVisible();
    // github is wanted in Claude Code only: Cursor gets "remove if present", and so does search everywhere.
    await expect(page.getByRole('heading', { level: 3, name: 'To remove, if present' })).toBeVisible();
    await expect(
      page.getByRole('region', { name: 'github · Cursor' }).getByText(/If present, delete the entry/),
    ).toBeVisible();
  });

  test('the Captured tab asks to be given a decision, and nothing is written until a person accepts', async ({
    page,
  }) => {
    // The inbox is given a capture of its own: handing one over needs an assistant on a machine. The real pipeline is proved by the
    // integration tests; what a browser adds is the review, the form and what it sends.
    const accepted: unknown[] = [];
    await page.route('**/api/v1/mcp/captures', (route) =>
      route.fulfill({
        json: [
          {
            key: 'aaaaaaaaaaaa',
            state: 'new',
            matchId: null,
            matchName: null,
            server: {
              name: 'Notes Tool',
              transport: 'http',
              url: 'https://notes.homelab.example/mcp',
              command: null,
              args: [],
              headers: [{ name: 'X-Api-Key', variable: null, literal: null }],
              env: [],
            },
            sightings: [
              {
                rowId: 1,
                workstation: 'dev-vm',
                workstationKnown: true,
                client: 'claude-code',
                scope: 'user',
                name: 'Notes Tool',
                receivedAt: new Date().toISOString(),
              },
            ],
            differences: [],
            proposal: {
              id: 'notes-tool',
              name: 'Notes Tool',
              scope: 'user',
              variables: [{ kind: 'header', name: 'X-Api-Key', variable: 'NOTES_TOOL_X_API_KEY', fromCapture: false }],
              yaml: 'mcpServers:\n  - id: notes-tool',
            },
          },
        ],
      }),
    );
    await page.route('**/api/v1/mcp/captures/*/accept', async (route) => {
      accepted.push(route.request().postDataJSON());
      await route.fulfill({
        json: {
          written: true,
          readOnly: false,
          serverId: 'notes-tool',
          workstationsUpdated: ['dev-vm'],
          skipped: [],
          errors: [],
          yaml: null,
        },
      });
    });

    await page.goto('/mcp-servers?tab=captured');
    const card = page.getByRole('listitem', { name: 'Notes Tool' });
    await expect(card.getByText('New', { exact: true })).toBeVisible();
    await expect(card.getByText(/Seen on dev-vm · claude-code/)).toBeVisible();
    expect(accepted).toHaveLength(0);

    await card.getByRole('button', { name: 'Add to the catalog…' }).click();
    await expect(card.getByLabel('header X-Api-Key: variable that holds it')).toHaveValue('NOTES_TOOL_X_API_KEY');
    await card.getByRole('button', { name: 'Add to the catalog' }).click();

    await expect(card.getByText('Written to the catalog as notes-tool.')).toBeVisible();
    expect(accepted).toEqual([
      {
        mode: 'new',
        id: 'notes-tool',
        name: 'Notes Tool',
        variables: { 'header:X-Api-Key': 'NOTES_TOOL_X_API_KEY' },
        addToWorkstations: true,
      },
    ]);
  });
});
