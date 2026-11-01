import assert from 'node:assert/strict';
import test from 'node:test';

import { loadConfig } from '../src/config.ts';

const base = {
  PORTARR_AGENT_SECRET: 's'.repeat(40),
  PORTARR_MCP_URL: 'http://portarr.example:8484/mcp',
};

test("Portarr's MCP key is optional: the runner uses the token Portarr mints for each message", () => {
  assert.equal(loadConfig(base).portarrMcpKey, undefined);
  assert.equal(loadConfig({ ...base, PORTARR_MCP_APIKEY: '  ' }).portarrMcpKey, undefined);
});

test('a key that is set must still be strong enough', () => {
  assert.equal(loadConfig({ ...base, PORTARR_MCP_APIKEY: 'k'.repeat(40) }).portarrMcpKey, 'k'.repeat(40));
  assert.throws(() => loadConfig({ ...base, PORTARR_MCP_APIKEY: 'short' }), /PORTARR_MCP_APIKEY, when set/);
});

test('the models offered and the default one can be set, and are checked', () => {
  const config = loadConfig({
    ...base,
    PORTARR_AGENT_MODELS: 'sonnet,opus:Opus Next',
    PORTARR_AGENT_DEFAULT_MODEL: 'opus',
  });

  assert.deepEqual(config.models, [
    { id: 'sonnet', label: 'sonnet' },
    { id: 'opus', label: 'Opus Next' },
  ]);
  assert.equal(config.defaultModel, 'opus');
  assert.equal(loadConfig(base).models, undefined);
  assert.throws(() => loadConfig({ ...base, PORTARR_AGENT_MODELS: 'Sonnet 4' }), /not a model name/);
  assert.throws(
    () => loadConfig({ ...base, PORTARR_AGENT_MODELS: 'sonnet', PORTARR_AGENT_DEFAULT_MODEL: 'opus' }),
    /not one of PORTARR_AGENT_MODELS/,
  );
});

test('the number of kept conversations has a default and bounds', () => {
  assert.equal(loadConfig(base).maxConversations, 200);
  assert.equal(loadConfig({ ...base, PORTARR_AGENT_MAX_CONVERSATIONS: '50' }).maxConversations, 50);
  assert.throws(() => loadConfig({ ...base, PORTARR_AGENT_MAX_CONVERSATIONS: '0' }), /between 1 and 5000/);
});
