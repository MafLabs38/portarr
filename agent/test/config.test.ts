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
