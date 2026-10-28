import { useMutation, useQueryClient } from '@tanstack/react-query';

import { ApiError } from './useApiQuery';

interface ApiMutationOptions {
  /** Path below /api/v1, e.g. `/system/logging`. */
  path: string;
  method: 'POST' | 'PUT' | 'DELETE';
}

/**
 * The only way components write server data (DESIGN.md §1.1). On success, the response replaces
 * the cached query of the same path, so readers update without refetching.
 */
export default function useApiMutation<TBody, TResult>({ path, method }: ApiMutationOptions) {
  const queryClient = useQueryClient();

  return useMutation<TResult, ApiError, TBody>({
    mutationFn: async (body) => {
      const response = await fetch(`/api/v1${path}`, {
        method,
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!response.ok) {
        throw new ApiError(response.status, path);
      }

      return (await response.json()) as TResult;
    },
    onSuccess: (result) => {
      queryClient.setQueryData(['api', path], result);
    },
  });
}
