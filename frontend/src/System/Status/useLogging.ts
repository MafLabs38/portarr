import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type Logging from 'typings/Logging';

const path = '/system/logging';

export function useLogging() {
  return useApiQuery<Logging>({ path });
}

export function useUpdateLogging() {
  return useApiMutation<Logging, Logging>({ path, method: 'PUT' });
}
