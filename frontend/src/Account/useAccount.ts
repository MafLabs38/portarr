import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { ApiKey, CreatedApiKey, Me } from 'typings/Auth';

const keysPath = '/auth/keys';

export function useUpdateProfile() {
  return useApiMutation<{ displayName: string }, Me>({
    path: '/auth/profile',
    method: 'PUT',
    invalidate: ['/auth/me'],
  });
}

export function useUploadAvatar() {
  return useApiMutation<{ file: File }, Me>({
    path: '/auth/avatar',
    method: 'PUT',
    invalidate: ['/auth/me'],
    serialize: ({ file }) => {
      const form = new FormData();
      form.append('file', file);
      return form;
    },
  });
}

export function useRemoveAvatar() {
  return useApiMutation<undefined, Me>({ path: '/auth/avatar', method: 'DELETE', invalidate: ['/auth/me'] });
}

export function useChangePassword() {
  return useApiMutation<{ currentPassword: string; newPassword: string }, undefined>({
    path: '/auth/password',
    method: 'PUT',
    invalidate: [],
  });
}

export function useApiKeys() {
  return useApiQuery<ApiKey[]>({ path: keysPath });
}

export function useCreateApiKey() {
  return useApiMutation<{ name: string; scope: 'read' | 'full'; expires?: string }, CreatedApiKey>({
    path: keysPath,
    method: 'POST',
    invalidate: [keysPath],
  });
}

export function useRevokeApiKey() {
  return useApiMutation<{ id: number }, ApiKey>({
    path: ({ id }) => `${keysPath}/${id}`,
    method: 'DELETE',
    invalidate: [keysPath],
  });
}
