export default interface CatalogStatus {
  filePath: string;
  fileExists: boolean;
  loadedAt: string | null;
  errors: string[];
  apps: number;
  stacks: number;
  environments: number;
}
