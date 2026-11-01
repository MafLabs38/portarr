/** Who is signed in (V7): the account, its role and the permissions the UI shows pages for. */
export interface Me {
  id: number | null;
  username: string;
  displayName: string;
  role: string;
  permissions: string[];
  avatarUrl: string | null;
  authMethod: string;
}

export interface AuthStatus {
  setupRequired: boolean;
  name: string;
  logoUrl: string | null;
  /** A fresh install asks for the one-time code printed in the Portarr log. */
  setupCodeRequired?: boolean;
}

export interface UserAccount {
  id: number;
  username: string;
  displayName: string;
  roleId: number;
  role: string;
  avatarUrl: string | null;
  created: string;
  lastLogin: string | null;
  disabled: boolean;
  lockedUntil: string | null;
}

export interface Role {
  id: number;
  name: string;
  description: string | null;
  builtIn: boolean;
  permissions: string[];
  users: number;
}

export interface PermissionInfo {
  key: string;
  area: string;
  reserved: boolean;
}

export interface RoleList {
  roles: Role[];
  permissions: PermissionInfo[];
}

export interface ApiKey {
  id: number;
  name: string;
  prefix: string;
  scope: 'read' | 'full';
  created: string;
  lastUsed: string | null;
  expires: string | null;
  revoked: string | null;
  active: boolean;
}

export interface CreatedApiKey {
  key: ApiKey;
  value: string;
}

export interface AuthActivity {
  at: string;
  event: string;
  actor: string | null;
  subject: string | null;
  detail: string | null;
  client: string | null;
}
