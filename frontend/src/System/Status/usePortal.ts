import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type Portal from 'typings/Portal';
import type { PortalColors } from 'typings/Portal';

const path = '/portal';

export function usePortal() {
  return useApiQuery<Portal>({ path });
}

/** Catalog writes are keyed until Portarr has accounts (D-85): the key is typed, used once, never stored. */
const keyHeader = ({ apiKey }: { apiKey: string }) => ({ 'X-Api-Key': apiKey });

/** Only what is given is sent: a missing name or color stays as it is in the catalog, an empty one goes back to the default. */
export function useSetPortal() {
  return useApiMutation<{ name?: string; colors?: Partial<PortalColors>; apiKey: string }, unknown>({
    path,
    method: 'PUT',
    invalidate: [path],
    headers: keyHeader,
    serialize: ({ name, colors }) => JSON.stringify({ name, colors }),
  });
}

export function useUploadPortalLogo() {
  return useApiMutation<{ file: File; apiKey: string }, unknown>({
    path: `${path}/logo`,
    method: 'PUT',
    invalidate: [path],
    headers: keyHeader,
    serialize: ({ file }) => {
      const form = new FormData();
      form.append('file', file);
      return form;
    },
  });
}
