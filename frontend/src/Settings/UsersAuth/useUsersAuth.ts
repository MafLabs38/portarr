import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { AuthActivity, RoleList, UserAccount } from 'typings/Auth';

const usersPath = '/users';
const rolesPath = '/roles';
const activityPath = '/users/activity';

export function useUsers() {
  return useApiQuery<UserAccount[]>({ path: usersPath });
}

export function useRoles() {
  return useApiQuery<RoleList>({ path: rolesPath });
}

export function useActivity(enabled: boolean) {
  return useApiQuery<AuthActivity[]>({ path: activityPath, enabled, refetchInterval: 30_000 });
}

export function useCreateUser() {
  return useApiMutation<{ username: string; displayName?: string; password: string; roleId: number }, UserAccount>({
    path: usersPath,
    method: 'POST',
    invalidate: [usersPath, rolesPath, activityPath],
  });
}

export function useUpdateUser() {
  return useApiMutation<{ id: number; displayName?: string; roleId?: number; disabled?: boolean }, UserAccount>({
    path: ({ id }) => `${usersPath}/${id}`,
    method: 'PUT',
    invalidate: [usersPath, rolesPath, activityPath],
    serialize: ({ displayName, roleId, disabled }) => JSON.stringify({ displayName, roleId, disabled }),
  });
}

export function useResetPassword() {
  return useApiMutation<{ id: number; password: string }, UserAccount>({
    path: ({ id }) => `${usersPath}/${id}/password`,
    method: 'POST',
    invalidate: [usersPath, activityPath],
    serialize: ({ password }) => JSON.stringify({ password }),
  });
}

export function useDeleteUser() {
  return useApiMutation<{ id: number }, UserAccount>({
    path: ({ id }) => `${usersPath}/${id}`,
    method: 'DELETE',
    invalidate: [usersPath, rolesPath, activityPath],
  });
}

interface RoleBody {
  name: string;
  description?: string;
  permissions: string[];
}

export function useCreateRole() {
  return useApiMutation<RoleBody, unknown>({ path: rolesPath, method: 'POST', invalidate: [rolesPath, activityPath] });
}

export function useUpdateRole() {
  return useApiMutation<RoleBody & { id: number }, unknown>({
    path: ({ id }) => `${rolesPath}/${id}`,
    method: 'PUT',
    invalidate: [rolesPath, usersPath, activityPath],
    serialize: ({ name, description, permissions }) => JSON.stringify({ name, description, permissions }),
  });
}

export function useDeleteRole() {
  return useApiMutation<{ id: number }, unknown>({
    path: ({ id }) => `${rolesPath}/${id}`,
    method: 'DELETE',
    invalidate: [rolesPath, activityPath],
  });
}
