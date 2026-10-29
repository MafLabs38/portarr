export default interface CatalogStatus {
  fileName: string;
  fileExists: boolean;
  loadedAt: string | null;
  errors: string[];
  apps: number;
  stacks: number;
  environments: number;
}
