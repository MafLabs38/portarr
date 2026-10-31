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
  /** Sign-in and sign-out: every cached answer belonged to another user (or none), so all queries are reset. */
  reset?: boolean;
  /**
   * A 401 to this call means the credentials were refused, not that the session ended (sign-in, first-run setup): the
   * app must not go and ask who is signed in again, which would unmount the form that shows the refusal.
   */
  refusalIsNotExpiry?: boolean;
}

/** Writes made with the session cookie carry this header (CSRF protection, V7): a cross-site form cannot add it. */
export const csrfHeaders = { 'X-Requested-With': 'portarr' };

/**
 * The only way components write server data (DESIGN.md §1.1). On success, the response replaces
 * the cached query of the same path, so readers update without refetching; with `invalidate`, the
 * listed queries are refetched instead (with `reset`, every query is). A response without a body (204) resolves to undefined.
 */
export default function useApiMutation<TBody, TResult>({
  path,
  method,
  invalidate,
  headers,
  serialize,
  reset,
  refusalIsNotExpiry,
}: ApiMutationOptions<TBody>) {
  const queryClient = useQueryClient();

  return useMutation<TResult, ApiError, TBody>({
    meta: { refusalIsNotExpiry: refusalIsNotExpiry === true },
    mutationFn: async (body) => {
      const target = typeof path === 'function' ? path(body) : path;
      const payload = serialize ? serialize(body) : JSON.stringify(body ?? {});
      const response = await fetch(`/api/v1${target}`, {
        method,
        // An upload sets its own Content-Type, boundary included.
        headers: {
          Accept: 'application/json',
          ...csrfHeaders,
          ...(payload instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
          ...headers?.(body),
        },
        body: payload,
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as { detail?: unknown } | null;
        throw new ApiError(response.status, target, typeof problem?.detail === 'string' ? problem.detail : undefined);
      }

      const text = await response.text();
      return (text ? JSON.parse(text) : undefined) as TResult;
    },
    onSuccess: async (result) => {
      if (reset) {
        await queryClient.resetQueries();
      } else if (invalidate) {
        await Promise.all(invalidate.map((key) => queryClient.invalidateQueries({ queryKey: ['api', key] })));
      } else if (typeof path === 'string') {
        queryClient.setQueryData(['api', path], result);
      }
    },
  });
}
