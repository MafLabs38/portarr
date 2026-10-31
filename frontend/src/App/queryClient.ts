import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';

import { ApiError } from 'Helpers/Hooks/useApiQuery';

/** The query that says who is signed in (V7). */
export const meKey = ['api', '/auth/me'];

function isUnauthorized(error: unknown) {
  return error instanceof ApiError && error.status === 401;
}

export function createQueryClient() {
  // An answer of 401 anywhere means the session ended: ask who is signed in again, which shows the login page.
  const expired = (error: unknown, key?: readonly unknown[]) => {
    if (isUnauthorized(error) && key?.[1] !== '/auth/me' && key?.[1] !== '/auth/status') {
      client.invalidateQueries({ queryKey: meKey }).catch(() => undefined);
    }
  };

  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: (error, query) => expired(error, query.queryKey) }),
    mutationCache: new MutationCache({
      // Wrong credentials at sign-in are not an ended session: the login form keeps its message.
      onError: (error, _variables, _context, mutation) => {
        if (mutation.meta?.refusalIsNotExpiry !== true) {
          expired(error);
        }
      },
    }),
    defaultOptions: {
      queries: {
        refetchOnWindowFocus: false,
        staleTime: 30_000,
        // A refusal (401, 403, 404) will not change by asking again.
        retry: (count, error) => count < 3 && !(error instanceof ApiError && [401, 403, 404].includes(error.status)),
      },
    },
  });

  return client;
}
