export type HealthStatus = 'Healthy' | 'Degraded' | 'Down' | 'Unknown' | 'Maintenance' | 'Paused';

export default interface Status {
  status: HealthStatus;
  /** A code the interface translates (`timeout`), or a sentence that is shown as it is. */
  reason: string;
  /** What the code is about: the HTTP status, the days left… */
  reasonArgs?: string[];
}
