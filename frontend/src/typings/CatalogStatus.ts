export default interface CatalogStatus {
  fileName: string;
  fileExists: boolean;
  loadedAt: string | null;
  errors: string[];
  apps: number;
  stacks: number;
  environments: number;
  /** The file's version as last read: sent back with a catalog edit (D-83). */
  version?: string | null;
  /** Whether the catalog can be edited through the API. */
  writable?: boolean;
}
