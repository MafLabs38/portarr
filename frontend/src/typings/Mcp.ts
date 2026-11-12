/** The state of an MCP server's check (D-114). `waiting`: no result yet; `not-checked`: a local process, or `check: false`. */
export type McpStatus = 'healthy' | 'authentication-required' | 'down' | 'unknown' | 'waiting' | 'not-checked';

export interface McpValue {
  /** `header` of a remote server, `env` of a local one. */
  kind: 'header' | 'env';
  name: string;
  /** A harmless text written in the catalog. */
  literal: string | null;
  /** Or the name of the variable that holds the value on the workstation. */
  variable: string | null;
  /** The key, in the operator's secret store, of the secret the workstation puts in that variable (D-117). */
  secret: string | null;
}

export interface McpHealth {
  state: string;
  since: string;
  lastHealthyAt: string | null;
  lastCheckedAt: string;
  /** Reason code of the checks when down: timeout, dns-failure, connection-failed, tls-invalid, http-502… */
  error: string | null;
  statusCode: number | null;
}

export interface McpServer {
  id: string;
  name: string;
  transport: 'http' | 'stdio';
  scope: 'user' | 'project' | 'local';
  url: string | null;
  command: string | null;
  arguments: string[];
  values: McpValue[];
  app: string | null;
  checkInterval: string | null;
  status: McpStatus;
  health: McpHealth | null;
  unpinnedPackage: boolean;
  notes: string | null;
}

export interface McpOperation {
  kind: 'command' | 'file' | 'remove' | 'provision';
  description: string;
  text: string;
  path: string | null;
}

export interface McpRendering {
  /** The id of the server this is for. */
  server: string;
  client: string;
  clientName: string;
  scope: string;
  supported: boolean;
  requiresEnv: string[];
  /** What the workstation must be able to read in the operator's secret store: `kv/mcp/memory`. */
  requiresSecrets: string[];
  operations: McpOperation[];
  notes: string[];
}

export interface McpServerDetail {
  server: McpServer;
  os: string;
  renderings: McpRendering[];
}

/** A server wanted on a workstation, in these clients, at this scope (null: the server's own). */
export interface McpWanted {
  server: string;
  clients: string[];
  scope: string | null;
}

export interface McpWorkstation {
  id: string;
  name: string;
  os: 'windows' | 'linux' | 'macos';
  /** The environment of the catalog this machine is too, when it is one (a dev VM). */
  environment: string | null;
  clients: string[];
  wanted: McpWanted[];
  notes: string | null;
}

/** What to install, what to remove if present and what the machine must provide, for one workstation. */
export interface McpWorkstationPlan {
  workstation: McpWorkstation;
  os: string;
  install: McpRendering[];
  remove: McpRendering[];
  requiresEnv: string[];
  requiresSecrets: string[];
}

/** A header or an environment entry of a captured server: a variable name or a harmless literal, never a secret value. */
export interface McpCapturedValue {
  name: string;
  variable: string | null;
  literal: string | null;
}

export interface McpCapturedServer {
  name: string;
  transport: 'http' | 'stdio';
  url: string | null;
  command: string | null;
  args: string[];
  headers: McpCapturedValue[];
  env: McpCapturedValue[];
}

/** A server seen on a workstation: one row of the inbox. */
export interface McpCaptureSighting {
  rowId: number;
  workstation: string;
  workstationKnown: boolean;
  client: string;
  scope: string;
  name: string;
  receivedAt: string;
}

/** A value of the proposed definition that needs the name of the variable that holds it. */
export interface McpProposedVariable {
  kind: 'header' | 'env';
  name: string;
  variable: string;
  fromCapture: boolean;
}

export type McpCaptureState = 'new' | 'same' | 'differs';

/** The same server seen on one or several workstations, compared with the catalog (D-116). */
export interface McpCaptureGroup {
  key: string;
  state: McpCaptureState;
  matchId: string | null;
  matchName: string | null;
  server: McpCapturedServer;
  sightings: McpCaptureSighting[];
  differences: string[];
  proposal: { id: string; name: string; scope: string; variables: McpProposedVariable[]; yaml: string };
  /** What was reviewed: sent back with the decision, which is refused if the server was handed over again meanwhile. */
  version: string;
}

export type McpAcceptMode = 'new' | 'replace' | 'existing';

/** What a person decided for a group. `key` goes in the path, the rest in the body. */
export interface McpAcceptRequest {
  key: string;
  mode: McpAcceptMode;
  id?: string;
  name?: string;
  variables?: Record<string, string>;
  addToWorkstations: boolean;
  version: string;
}

export interface McpAcceptResult {
  written: boolean;
  readOnly: boolean;
  serverId: string | null;
  workstationsUpdated: string[];
  skipped: string[];
  errors: string[];
  yaml: string | null;
}
