/** The sections of the catalog the editor changes (the catalog file's top-level lists). */
export const catalogSections = [
  'apps',
  'environments',
  'stacks',
  'data',
  'links',
  'providers',
  'mcpServers',
  'workstations',
] as const;

export type CatalogSectionName = (typeof catalogSections)[number];

/** An item as the catalog file holds it: the keys of docs/catalog.md, as JSON. */
export type CatalogItem = Record<string, unknown> & { id: string };

export interface CatalogSectionItems {
  version: string;
  section: CatalogSectionName;
  items: CatalogItem[];
}

/** `applied`, `unchanged`, `valid` (a check that would pass) or `pending` (waits for an administrator). */
export interface CatalogEditOutcome {
  status: 'applied' | 'unchanged' | 'valid' | 'pending';
  version: string;
  changes: string[];
  pendingId?: string | null;
}

/** A change that waits for approval: who asked, for which item, the item before and after. */
export interface PendingChange {
  id: string;
  at: string;
  channel: string;
  user?: string | null;
  via?: string | null;
  section: string;
  itemId: string;
  operation: string;
  before: CatalogItem | null;
  after: CatalogItem | null;
  changes: string[];
}
