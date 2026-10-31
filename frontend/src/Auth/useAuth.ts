import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { AuthStatus, Me } from 'typings/Auth';

/** Whether the first administrator still has to be created, and what the login page shows of the portal. */
export function useAuthStatus() {
  return useApiQuery<AuthStatus>({ path: '/auth/status', retry: false, staleTime: 0 });
}

/** Who is signed in; a 401 means nobody (the login page shows). */
export function useMe() {
  return useApiQuery<Me>({ path: '/auth/me', retry: false, staleTime: 60_000 });
}

export function useLogin() {
  return useApiMutation<{ username: string; password: string; remember: boolean }, Me>({
    path: '/auth/login',
    method: 'POST',
    reset: true,
    refusalIsNotExpiry: true,
  });
}

export function useSetup() {
  return useApiMutation<{ username: string; displayName?: string; password: string }, Me>({
    path: '/auth/setup',
    method: 'POST',
    reset: true,
    refusalIsNotExpiry: true,
  });
}

export function useLogout() {
  return useApiMutation<undefined, undefined>({ path: '/auth/logout', method: 'POST', reset: true });
}
