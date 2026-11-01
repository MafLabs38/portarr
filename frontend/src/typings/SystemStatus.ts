export default interface SystemStatus {
  appName: string;
  version: string;
  startTime: string;
  uptimeSeconds: number;
  runtimeVersion: string;
  osDescription: string;
  databaseEngine: string;
  dataFolder: string;
}
