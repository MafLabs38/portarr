import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type SystemStatus from 'typings/SystemStatus';

/** `refetchInterval` keeps a long-lived screen current, e.g. the version in the sidebar after an update. */
export default function useSystemStatus(options?: { refetchInterval?: number; enabled?: boolean }) {
  return useApiQuery<SystemStatus>({ path: '/system/status', ...options });
}
