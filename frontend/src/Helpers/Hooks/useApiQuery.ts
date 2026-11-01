import { useQuery, type UseQueryOptions } from '@tanstack/react-query';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    path: string,
    /** The problem details' `detail`, when the server gave one (safe to show: it never holds a secret). */
    public readonly detail?: string,
  ) {
    super(`Request to ${path} failed with status ${status}`);
    this.name = 'ApiError';
  }
}

interface ApiQueryOptions<T> extends Omit<UseQueryOptions<T, ApiError>, 'queryKey' | 'queryFn'> {
  /** Path below /api/v1, e.g. `/system/status`. */
  path: string;
}

/** The only way components read server data (DESIGN.md §1.1). */
export default function useApiQuery<T>({ path, ...options }: ApiQueryOptions<T>) {
  return useQuery<T, ApiError>({
    queryKey: ['api', path],
    queryFn: async ({ signal }) => {
      const response = await fetch(`/api/v1${path}`, { signal, headers: { Accept: 'application/json' } });

      if (!response.ok) {
        throw new ApiError(response.status, path);
      }

      return (await response.json()) as T;
    },
    ...options,
  });
}
