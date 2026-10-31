import type { AppCheck } from './Check';
import type Status from './Status';

export interface AppUrl {
  label: string;
  href: string;
  scope: 'public' | 'lan';
  /** Built from a published port rather than declared. */
  derived: boolean;
}

export interface AppStack {
  id: string;
  name: string;
  role: 'critical' | 'optional';
}

export interface AppWorkload {
  environmentId: string;
  /** Connector kind of its environment: docker, cloudflare or none. */
  runtime: string;
  name: string;
  role: 'critical' | 'optional';
  found: boolean;
  state: string | null;
  health: 'none' | 'starting' | 'healthy' | 'unhealthy' | null;
  statusText: string | null;
  status: Status;
}

export default interface App {
  id: string;
  name: string;
  type: 'web' | 'desktop' | 'service';
  icon: string | null;
  description: string | null;
  tags: string[];
  urls: AppUrl[];
  stack: AppStack | null;
  status: Status;
  workloads: AppWorkload[];
  /** Null: the app is not monitored (not a web app, no URL, or `check: false`). */
  check: AppCheck | null;
}
