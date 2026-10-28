import '@testing-library/jest-dom/vitest';

import { cleanup } from '@testing-library/react';

import { server } from './__mocks__/server';

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => server.close());
