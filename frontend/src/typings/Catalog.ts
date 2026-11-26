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

/** `applied`, `unchanged` or `valid` (a check that would pass). */
export interface CatalogEditOutcome {
  status: 'applied' | 'unchanged' | 'valid';
  version: string;
  changes: string[];
}
