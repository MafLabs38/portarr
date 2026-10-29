export type HealthStatus = 'Healthy' | 'Degraded' | 'Down' | 'Unknown' | 'Maintenance' | 'Paused';

export default interface Status {
  status: HealthStatus;
  reason: string;
}
