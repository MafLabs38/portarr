import { expect, test } from '@playwright/test';

// V6: a whole turn of the built-in chat. The runner is a recorded fake (tests/e2e/stack/wiremock/mappings/conversation-*.json):
// no model is ever called, but everything between the panel and the runner is real: the conversation is created and owned,
// the message is sent, the event stream is relayed, and the answer to a proposed change goes back to the runner.
test.describe('Chat journey', { tag: '@smoke' }, () => {
  test('a message starts a conversation: a tool runs, the answer streams in, and a proposed change waits for the operator', async ({
    page,
  }) => {
    await page.goto('/apps');
    await page.getByRole('button', { name: 'Chat' }).click();
    const panel = page.getByRole('complementary', { name: 'Chat' });
    await panel.getByRole('textbox', { name: 'Message' }).fill('What needs my attention?');

    const isChat = (path: string, method: string) => (response: import('@playwright/test').Response) =>
      new URL(response.url()).pathname.endsWith(path) && response.request().method() === method;
    const [created, sent] = await Promise.all([
      page.waitForResponse(isChat('/assistant/chat/conversations', 'POST')),
      page.waitForResponse(isChat('/messages', 'POST')),
      panel.getByRole('button', { name: 'Send' }).click(),
    ]);
    expect(created.status()).toBe(201);
    expect(sent.status()).toBe(202);

    // The recorded turn: the question, a tool call that finished, the answer.
    await expect(panel.getByText('What needs my attention?').first()).toBeVisible();
    const tool = panel.locator('details', { hasText: 'get_overview' });
    await expect(tool.getByText('done')).toBeVisible();
    await tool.getByText('get_overview').click();
    await expect(tool.getByText('2 apps need attention')).toBeVisible();
    await expect(panel.getByText('Two apps are degraded; I can write a note on the first.')).toBeVisible();

    // A change the agent proposes waits: nothing is written before the operator answers.
    const card = panel.getByRole('region', { name: 'Apply this change?' });
    await expect(card).toContainText('set_notes');
    await expect(card).toContainText('degraded since 12:00');
    const [answer] = await Promise.all([
      page.waitForResponse(isChat('/permissions/r1', 'POST')),
      card.getByRole('button', { name: 'Reject' }).click(),
    ]);
    expect(answer.status()).toBe(200);
    expect(answer.request().postDataJSON()).toMatchObject({ approve: false });
  });
});
