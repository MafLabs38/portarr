import { guidedFields } from 'Settings/Catalog/itemDraft';

// The keys each section's document accepts for these simple fields (CatalogDocument.cs, McpDocument.cs): the catalog parser refuses
// any other key, so a guided field outside this list would make the first form a user fills in fail.
const accepted: Record<string, string[]> = {
  apps: ['name', 'icon', 'type', 'stack', 'description'],
  environments: ['name', 'address', 'host', 'notes'],
  stacks: ['name', 'description'],
  data: ['name', 'app', 'kind'],
  links: ['name', 'kind', 'notes'],
  providers: ['name', 'kind', 'account'],
  mcpServers: ['name', 'transport'],
  workstations: ['name', 'os'],
};

describe('guided fields', () => {
  it.each(Object.entries(guidedFields))('offers only keys the %s document accepts', (section, fields) => {
    expect(fields.map((field) => field.key).filter((key) => !(accepted[section] ?? []).includes(key))).toEqual([]);
  });
});
