import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';

import { server } from '__mocks__/server';
import useApiMutation from 'Helpers/Hooks/useApiMutation';

const wrapper = ({ children }: Readonly<{ children: ReactNode }>) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
    {children}
  </QueryClientProvider>
);

describe('useApiMutation', () => {
  it('sends no body and no JSON content type when there is none', async () => {
    let seen: { body: string; contentType: string | null } | undefined;
    server.use(
      http.delete('/api/v1/thing', async ({ request }) => {
        seen = { body: await request.text(), contentType: request.headers.get('Content-Type') };
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { result } = renderHook(() => useApiMutation<undefined, undefined>({ path: '/thing', method: 'DELETE' }), {
      wrapper,
    });

    result.current.mutate(undefined);

    await waitFor(() => expect(seen).toBeDefined());
    expect(seen).toEqual({ body: '', contentType: null });
  });

  it('sends a body as JSON', async () => {
    let seen: { body: unknown; contentType: string | null } | undefined;
    server.use(
      http.post('/api/v1/thing', async ({ request }) => {
        seen = { body: await request.json(), contentType: request.headers.get('Content-Type') };
        return HttpResponse.json({});
      }),
    );
    const { result } = renderHook(() => useApiMutation<{ a: number }, unknown>({ path: '/thing', method: 'POST' }), {
      wrapper,
    });

    result.current.mutate({ a: 1 });

    await waitFor(() => expect(seen).toBeDefined());
    expect(seen).toEqual({ body: { a: 1 }, contentType: 'application/json' });
  });
});
