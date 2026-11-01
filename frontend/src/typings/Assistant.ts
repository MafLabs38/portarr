export interface AssistantTool {
  name: string;
  title: string | null;
  description: string | null;
  /** False for the catalog write tools (D-86). */
  readOnly: boolean;
}

export interface AssistantResource {
  uri: string;
  name: string;
  description: string | null;
}

/** The MCP server (V5). `path` is relative to the address Portarr is served from; the key is never returned. */
export default interface AssistantStatus {
  enabled: boolean;
  path: string;
  keyConfigured: boolean;
  headerName: string;
  tools: AssistantTool[];
  resources: AssistantResource[];
  /** The catalog write tools are registered (PORTARR__MCP__WRITETOOLS). */
  writeTools: boolean;
  /** Catalog edits are on (PORTARR__CATALOG__WRITABLE). */
  catalogWritable: boolean;
}

/** One audited catalog edit (D-85): through the API or the MCP write tools. */
export interface CatalogAuditEntry {
  at: string;
  channel: string;
  client: string | null;
  operation: string;
  section: string;
  id: string;
  versionBefore: string;
  versionAfter: string;
  changes: string[];
  /** Where an MCP edit came from, e.g. `chat:<conversation id>` for the built-in chat (D-89). */
  via?: string | null;
}

export interface Knowledge {
  uri: string;
  generatedAt: string;
  markdown: string;
}
