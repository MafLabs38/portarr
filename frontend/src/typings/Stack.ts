import type Status from './Status';

export interface StackMember {
  appId: string;
  name: string;
  role: 'critical' | 'optional';
  status: Status;
}

export default interface Stack {
  id: string;
  name: string;
  description: string | null;
  status: Status;
  members: StackMember[];
}
