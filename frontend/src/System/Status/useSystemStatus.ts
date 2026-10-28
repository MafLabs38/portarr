import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type SystemStatus from 'typings/SystemStatus';

export default function useSystemStatus() {
  return useApiQuery<SystemStatus>({ path: '/system/status' });
}
