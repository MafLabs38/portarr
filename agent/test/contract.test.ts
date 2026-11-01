import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import { newManager } from './fakes.ts';

// The e2e stack answers GET /v1/agents from a WireMock mapping written by hand. It must keep the shape the runner
// answers with, or the e2e journey would pass against a runner that does not exist.
test('the e2e stub of /v1/agents has the shape the runner answers with', async () => {
  const mapping = JSON.parse(
    readFileSync(new URL('../../tests/e2e/stack/wiremock/mappings/agents.json', import.meta.url), 'utf8'),
  ) as {
    response: { jsonBody: Record<string, unknown>[] };
  };
  const { manager } = await newManager();
  const real = manager.agents() as unknown as Record<string, unknown>[];

  const shape = (agent: Record<string, unknown>) => ({
    keys: Object.keys(agent).sort(),
    models: (agent.models as Record<string, unknown>[]).map((model) => Object.keys(model).sort()),
  });
  assert.deepEqual(shape(mapping.response.jsonBody[0]).keys, shape(real[0]).keys);
  assert.deepEqual(shape(mapping.response.jsonBody[0]).models[0], shape(real[0]).models[0]);
});
