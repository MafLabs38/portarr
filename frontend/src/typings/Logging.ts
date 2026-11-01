export type LogLevel = 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error';

export default interface Logging {
  level: LogLevel;
}
