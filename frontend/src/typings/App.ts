import type Status from './Status';

export interface AppUrl {
  label: string;
  href: string;
}

export interface AppStack {
  id: string;
  name: string;
  role: 'critical' | 'optional';
}

export interface AppWorkload {
  environmentId: string;
  name: string;
  found: boolean;
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
}
