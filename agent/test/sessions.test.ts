import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import type { ChatEventEnvelope } from '../src/events.ts';
import { classify } from '../src/policy.ts';
import { ChatError } from '../src/sessions.ts';
import { newManager } from './fakes.ts';

test('classify: Portarr reads run, Portarr writes ask, anything else is forbidden', () => {
  assert.equal(classify('mcp__portarr__get_overview'), 'read');
  assert.equal(classify('mcp__portarr__list_apps'), 'read');
  assert.equal(classify('mcp__portarr__find_workload'), 'read');
  assert.equal(classify('mcp__portarr__set_notes'), 'write');
  assert.equal(classify('mcp__portarr__remove_catalog_item'), 'write');
  assert.equal(classify('Bash'), 'forbidden');
  assert.equal(classify('mcp__other__get_secrets'), 'forbidden');
  assert.equal(classify('mcp__portarr__get_x; rm'), 'forbidden');
});

async function started() {
  const context = await newManager();
  const conversation = await context.manager.create(undefined, undefined, undefined);
  const events: ChatEventEnvelope[] = [];
  context.manager.subscribe(conversation.id, 0, (event) => events.push(event));
  context.manager.send(conversation.id, 'Add a note to the provider object-storage\nsecond line');
  return { ...context, id: conversation.id, events, process: context.adapter.last };
}

test('a message starts one process with a fresh session id, and titles the conversation', async () => {
  const { manager, process, id } = await started();
  assert.deepEqual(process.sent, ['Add a note to the provider object-storage\nsecond line']);
  assert.equal(process.options.resume, undefined);
  assert.match(process.options.sessionId, /^[0-9a-f-]{36}$/);
  assert.equal(manager.get(id).title, 'Add a note to the provider object-storage');
  assert.equal(manager.get(id).busy, true);
  assert.throws(
    () => manager.send(id, 'again'),
    (error: ChatError) => error.status === 409,
  );
});

test('a write waits for the operator; approving answers allow with the same input', async () => {
  const { manager, process, id, events } = await started();
  const input = { section: 'providers', id: 'object-storage', notes: 'x', version: '0123456789abcdef' };
  process.options.onEvent({ type: 'permission-request', requestId: 'r1', toolName: 'mcp__portarr__set_notes', input });

  assert.deepEqual(process.answers, []);
  assert.equal(manager.get(id).pendingPermissions, 1);
  assert.deepEqual(
    events
      .filter((event) => event.type === 'permission')
      .map(({ type, requestId, toolName }) => ({ type, requestId, toolName })),
    [{ type: 'permission', requestId: 'r1', toolName: 'mcp__portarr__set_notes' }],
  );

  manager.answer(id, 'r1', true);
  assert.deepEqual(process.answers, [{ requestId: 'r1', decision: { allow: true, input } }]);
  assert.ok(events.some((event) => event.type === 'permission-resolved' && event.approved));
  assert.throws(
    () => manager.answer(id, 'r1', true),
    (error: ChatError) => error.status === 404,
  );
});

test('rejecting answers deny; reads are allowed and other tools denied without asking', async () => {
  const { manager, process, id, events } = await started();
  process.options.onEvent({
    type: 'permission-request',
    requestId: 'w',
    toolName: 'mcp__portarr__upsert_app',
    input: {},
  });
  process.options.onEvent({
    type: 'permission-request',
    requestId: 'r',
    toolName: 'mcp__portarr__get_app',
    input: { app: 'blog' },
  });
  process.options.onEvent({ type: 'permission-request', requestId: 'b', toolName: 'Bash', input: { command: 'id' } });
  manager.answer(id, 'w', false);

  const decisions = Object.fromEntries(process.answers.map((answer) => [answer.requestId, answer.decision.allow]));
  assert.deepEqual(decisions, { r: true, b: false, w: false });
  assert.equal(events.filter((event) => event.type === 'permission').length, 1);
});

test('cancel refuses pending writes and interrupts the CLI', async () => {
  const { manager, process, id, events } = await started();
  process.options.onEvent({
    type: 'permission-request',
    requestId: 'w',
    toolName: 'mcp__portarr__add_link',
    input: {},
  });
  manager.cancel(id);
  assert.equal(process.interrupted, 1);
  assert.equal(process.answers[0].decision.allow, false);
  assert.ok(
    events.some((event) => event.type === 'permission-resolved' && !event.approved && event.reason === 'cancelled'),
  );
});

test('stored events leave out live-only ones; a reopened conversation resumes the CLI session', async () => {
  const { manager, adapter, process, id, dataDir } = await started();
  process.options.onEvent({ type: 'session', agentSessionId: process.options.sessionId });
  process.options.onEvent({ type: 'text-delta', text: 'Do' });
  process.options.onEvent({ type: 'text', text: 'Done.' });
  process.options.onEvent({ type: 'turn-end', isError: false });
  assert.equal(manager.get(id).busy, false);

  const stored = manager.get(id).events.map((event) => event.type);
  assert.deepEqual(stored, ['user', 'text', 'turn-end']);

  // The process stops (idle timeout, restart…): the next message resumes the same CLI session.
  process.close();
  manager.send(id, 'And now?');
  assert.equal(adapter.processes.length, 2);
  assert.equal(adapter.last.options.resume, process.options.sessionId);

  await new Promise((resolve) => setTimeout(resolve, 50));
  const file = JSON.parse(readFileSync(`${dataDir}/conversations/${id}.json`, 'utf8'));
  assert.equal(file.agentSessionId, process.options.sessionId);
  assert.ok(!file.events.some((event: { type: string }) => event.type === 'text-delta' || event.type === 'status'));
});

test('a subscriber after a seq gets only the newer events', async () => {
  const { manager, process, id } = await started();
  process.options.onEvent({ type: 'text', text: 'one' });
  const replayed: ChatEventEnvelope[] = [];
  manager.subscribe(id, 1, (event) => replayed.push(event));
  assert.deepEqual(
    replayed.map((event) => event.type),
    ['text', 'status'],
  );
});

test('at most maxSessions processes: the idle one makes room, busy ones refuse', async () => {
  const { manager, adapter } = await newManager(1);
  const first = await manager.create(undefined, undefined, 'one');
  const second = await manager.create(undefined, undefined, 'two');
  manager.send(first.id, 'hello');
  assert.throws(
    () => manager.send(second.id, 'hello'),
    (error: ChatError) => error.status === 429,
  );
  adapter.last.options.onEvent({ type: 'turn-end', isError: false });
  manager.send(second.id, 'hello');
  assert.equal(adapter.processes[0].closed, true);
  assert.equal(manager.list().filter((conversation) => conversation.running).length, 1);
});

test('unknown agent or model is a 400', async () => {
  const { manager } = await newManager();
  await assert.rejects(manager.create('nope', undefined, undefined), (error: ChatError) => error.status === 400);
  await assert.rejects(manager.create(undefined, 'huge', undefined), (error: ChatError) => error.status === 400);
});

function mcpKeyOf(process: { options: { mcpConfigFile: string } }): string {
  const config = JSON.parse(readFileSync(process.options.mcpConfigFile, 'utf8')) as {
    mcpServers: Record<string, { headers: Record<string, string> }>;
  };
  return Object.values(config.mcpServers)[0].headers['X-Api-Key'];
}

test("a process uses the user's token as its MCP key, else the shared key", async () => {
  const { manager, adapter } = await newManager();
  const withToken = await manager.create(undefined, undefined, undefined);
  manager.send(withToken.id, 'hello', undefined, `ptc_${'a'.repeat(43)}`);
  assert.equal(mcpKeyOf(adapter.last), `ptc_${'a'.repeat(43)}`);

  const without = await manager.create(undefined, undefined, undefined);
  manager.send(without.id, 'hello');
  assert.equal(mcpKeyOf(adapter.last), 'k'.repeat(40));
});

test('a message with neither a token nor a shared key is refused instead of reaching Portarr without credentials', async () => {
  const { manager } = await newManager(2, null);
  const conversation = await manager.create(undefined, undefined, undefined);
  assert.throws(
    () => manager.send(conversation.id, 'hello'),
    (error: ChatError) => error.status === 400,
  );
});
