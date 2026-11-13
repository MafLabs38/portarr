import { expect, test } from '@playwright/test';

// The operator's MCP servers and the workstations. The stack's catalog declares three servers (a stub
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
});
