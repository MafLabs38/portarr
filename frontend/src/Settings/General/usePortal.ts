import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type Portal from 'typings/Portal';
import type { PortalColors } from 'typings/Portal';

const path = '/portal';

export function usePortal() {
  return useApiQuery<Portal>({ path });
}

/** Only what is given is sent: a missing name or color stays as it is in the catalog, an empty one goes back to the default. */
export function useSetPortal() {
  return useApiMutation<{ name?: string; colors?: Partial<PortalColors> }, unknown>({
    path,
    method: 'PUT',
    invalidate: [path],
    serialize: ({ name, colors }) => JSON.stringify({ name, colors }),
  });
}

export function useUploadPortalLogo() {
  return useApiMutation<{ file: File }, unknown>({
    path: `${path}/logo`,
    method: 'PUT',
    invalidate: [path],
    serialize: ({ file }) => {
      const form = new FormData();
      form.append('file', file);
      return form;
    },
  });
}
