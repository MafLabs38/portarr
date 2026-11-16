import type { CatalogItem, CatalogSectionName } from 'typings/Catalog';

/**
 * The simple fields offered as inputs for each section; the rest of an item is edited in its JSON. Each key is one the section's
 * document has (CatalogDocument.cs, McpDocument.cs): the parser refuses a key it does not know.
 */
export const guidedFields: Record<CatalogSectionName, { key: string; label: string; options?: string[] }[]> = {
  apps: [
    { key: 'name', label: 'Name' },
    { key: 'icon', label: 'Icon' },
    { key: 'type', label: 'Type', options: ['', 'web', 'desktop', 'service'] },
    { key: 'stack', label: 'Stack' },
    { key: 'description', label: 'Description' },
  ],
  environments: [
    { key: 'name', label: 'Name' },
    { key: 'address', label: 'Address' },
    { key: 'host', label: 'HypervisorHost' },
    { key: 'notes', label: 'Notes' },
  ],
  stacks: [
    { key: 'name', label: 'Name' },
    { key: 'description', label: 'Description' },
  ],
  data: [
    { key: 'name', label: 'Name' },
    { key: 'app', label: 'App' },
    { key: 'kind', label: 'Kind' },
  ],
  links: [
    { key: 'name', label: 'Name' },
    { key: 'kind', label: 'Kind', options: ['', 'wireguard-tunnel', 'vpn', 'lan', 'peering', 'ssh-tunnel', 'other'] },
    { key: 'notes', label: 'Notes' },
  ],
  providers: [
    { key: 'name', label: 'Name' },
    {
      key: 'kind',
      label: 'Kind',
      options: [
        '',
        'cdn',
        'paas',
        'dns',
        'registrar',
        'cloud',
        'vps',
        'storage',
        'backup',
        'email',
        'git',
        'registry',
        'monitoring',
        'other',
      ],
    },
    { key: 'account', label: 'Account' },
  ],
  mcpServers: [
    { key: 'name', label: 'Name' },
    { key: 'transport', label: 'Transport', options: ['', 'http', 'stdio'] },
  ],
  workstations: [
    { key: 'name', label: 'Name' },
    { key: 'os', label: 'McpOperatingSystem', options: ['', 'windows', 'linux', 'macos'] },
  ],
};

export type ParsedDraft = { item: CatalogItem } | { error: string };

/** The item the text holds, or why it is not one. */
export function parseDraft(text: string): ParsedDraft {
  try {
    const value: unknown = JSON.parse(text);
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      return { error: 'NotAnObject' };
    }
    return { item: value as CatalogItem };
  } catch {
    return { error: 'NotValidJson' };
  }
}

export function formatDraft(item: Record<string, unknown>): string {
  return JSON.stringify(item, null, 2);
}

/** The item with one top-level text key set, or removed when the text is empty: the order of the other keys is kept. */
export function withField(item: CatalogItem, key: string, value: string): CatalogItem {
  const entries = Object.entries(item).filter(([name]) => name !== key);
  if (value === '') {
    return Object.fromEntries(entries) as CatalogItem;
  }
  const kept = key in item;
  // A key that was already there keeps its place; a new one goes at the end.
  return Object.fromEntries(
    kept ? Object.entries(item).map(([name, v]) => [name, name === key ? value : v]) : [...entries, [key, value]],
  ) as CatalogItem;
}

/** Ids are lowercase letters, digits and dashes (docs/catalog.md). */
export const idPattern = /^[a-z0-9]+(-[a-z0-9]+)*$/;
