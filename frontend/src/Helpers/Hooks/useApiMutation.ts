import { useMutation, useQueryClient } from '@tanstack/react-query';

import { ApiError } from './useApiQuery';

interface ApiMutationOptions<TBody> {
  /** Path below /api/v1, e.g. `/system/logging`; a function when the path depends on the body. */
  path: string | ((body: TBody) => string);
  method: 'POST' | 'PUT' | 'DELETE';
  /** Paths whose cached queries are refetched on success, instead of caching the response under `path`. */
  invalidate?: string[];
  /** Extra headers for a body, e.g. the API key of a catalog write (D-85). */
  headers?: (body: TBody) => Record<string, string>;
  /** What is sent instead of the body as JSON: a subset of it, or an upload (FormData, multipart). */
  serialize?: (body: TBody) => string | FormData;
}

/**
 * The only way components write server data (DESIGN.md §1.1). On success, the response replaces
 * the cached query of the same path, so readers update without refetching; with `invalidate`, the
 * listed queries are refetched instead. A response without a body (204) resolves to undefined.
 */
export default function useApiMutation<TBody, TResult>({
  path,
  method,
  invalidate,
  headers,
  serialize,
}: ApiMutationOptions<TBody>) {
  const queryClient = useQueryClient();

  return useMutation<TResult, ApiError, TBody>({
    mutationFn: async (body) => {
      const target = typeof path === 'function' ? path(body) : path;
      const payload = serialize ? serialize(body) : JSON.stringify(body ?? {});
      const response = await fetch(`/api/v1${target}`, {
        method,
        // An upload sets its own Content-Type, boundary included.
        headers: {
          Accept: 'application/json',
          ...(payload instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
          ...headers?.(body),
        },
        body: payload,
      });

      if (!response.ok) {
        throw new ApiError(response.status, target);
      }

      const text = await response.text();
      return (text ? JSON.parse(text) : undefined) as TResult;
    },
    onSuccess: async (result) => {
      if (invalidate) {
        await Promise.all(invalidate.map((key) => queryClient.invalidateQueries({ queryKey: ['api', key] })));
      } else if (typeof path === 'string') {
        queryClient.setQueryData(['api', path], result);
      }
    },
  });
}
