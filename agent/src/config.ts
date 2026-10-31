// The runner's settings, from its environment (D-87). Secrets (the shared secret, Portarr's MCP key, the
// Claude token) are read here and never logged.

export interface RunnerConfig {
  host: string;
  port: number;
  /** Shared with Portarr (PORTARR__AGENT__SECRET): every request but /healthz carries it as a bearer token. */
  secret: string;
  dataDir: string;
  portarrMcpUrl: string;
  portarrMcpKey: string;
  maxSessions: number;
  idleTimeoutMs: number;
  claudeCommand: string;
}

export const minimumSecretLength = 32;

function integer(value: string | undefined, fallback: number, min: number, max: number): number {
  const parsed = value ? Number.parseInt(value, 10) : fallback;
  if (!Number.isFinite(parsed) || parsed < min || parsed > max) {
    throw new Error(`Expected an integer between ${min} and ${max}, got '${value}'.`);
  }
  return parsed;
}

export function loadConfig(env: NodeJS.ProcessEnv): RunnerConfig {
  const errors: string[] = [];
  const secret = env.PORTARR_AGENT_SECRET ?? '';
  if (secret.trim().length < minimumSecretLength) {
    errors.push(`PORTARR_AGENT_SECRET must be at least ${minimumSecretLength} characters (openssl rand -hex 32).`);
  }
  const mcpUrl = env.PORTARR_MCP_URL ?? '';
  if (!/^https?:\/\/[^\s]+\/mcp$/.test(mcpUrl)) {
    errors.push("PORTARR_MCP_URL must be Portarr's MCP endpoint, e.g. http://portarr:8484/mcp.");
  }
  const mcpKey = env.PORTARR_MCP_APIKEY ?? '';
  if (mcpKey.trim().length < minimumSecretLength) {
    errors.push(
      `PORTARR_MCP_APIKEY must be Portarr's MCP key (PORTARR__MCP__APIKEY, ${minimumSecretLength} characters at least).`,
    );
  }
  if (errors.length > 0) {
    throw new Error(errors.join('\n'));
  }
  return {
    host: env.PORTARR_AGENT_HOST ?? '0.0.0.0',
    port: integer(env.PORTARR_AGENT_PORT, 8488, 1, 65535),
    secret,
    dataDir: env.PORTARR_AGENT_DATA ?? '/data',
    portarrMcpUrl: mcpUrl,
    portarrMcpKey: mcpKey,
    maxSessions: integer(env.PORTARR_AGENT_MAX_SESSIONS, 3, 1, 20),
    idleTimeoutMs: integer(env.PORTARR_AGENT_IDLE_MINUTES, 15, 1, 24 * 60) * 60_000,
    claudeCommand: env.CLAUDE_BIN ?? 'claude',
  };
}
