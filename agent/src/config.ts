// The runner's settings, from its environment (D-87). Secrets (the shared secret, Portarr's MCP key, the
// Claude token) are read here and never logged.

export interface RunnerConfig {
  host: string;
  port: number;
  /** Shared with Portarr (PORTARR__AGENT__SECRET): every request but /healthz carries it as a bearer token. */
  secret: string;
  dataDir: string;
  portarrMcpUrl: string;
  portarrMcpKey?: string;
  maxSessions: number;
  /** Conversations kept on disk; the oldest idle one makes room for a new one. */
  maxConversations: number;
  idleTimeoutMs: number;
  /** The models offered, in order, and the one used when none is chosen; undefined: the adapter's own. */
  models?: { id: string; label: string }[];
  defaultModel?: string;
  claudeCommand: string;
}

export const minimumSecretLength = 32;

/** `sonnet,opus:Opus 5` → ids with optional labels (`id` or `id:Label`), each id a short lowercase name. */
export function parseModels(value: string | undefined): { id: string; label: string }[] | undefined {
  if (!value?.trim()) {
    return undefined;
  }
  return value.split(',').map((entry) => {
    const [id, ...label] = entry.trim().split(':');
    if (!/^[a-z0-9][a-z0-9.-]{0,63}$/.test(id)) {
      throw new Error(
        `PORTARR_AGENT_MODELS: '${entry.trim()}' is not a model name (a short lowercase id, optionally id:Label).`,
      );
    }
    return { id, label: label.join(':').trim() || id };
  });
}

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
  // Optional: Portarr mints a token per message (D-98), which is what the runner normally uses. A shared key only
  // serves callers that send none, and it should be a per-user key, never an Admin-equivalent secret.
  const mcpKey = env.PORTARR_MCP_APIKEY?.trim() ? env.PORTARR_MCP_APIKEY : undefined;
  if (mcpKey !== undefined && mcpKey.trim().length < minimumSecretLength) {
    errors.push(`PORTARR_MCP_APIKEY, when set, must be at least ${minimumSecretLength} characters.`);
  }
  let models: { id: string; label: string }[] | undefined;
  try {
    models = parseModels(env.PORTARR_AGENT_MODELS);
  } catch (error) {
    errors.push((error as Error).message);
  }
  const defaultModel = env.PORTARR_AGENT_DEFAULT_MODEL?.trim() || undefined;
  if (defaultModel && models && !models.some((model) => model.id === defaultModel)) {
    errors.push(`PORTARR_AGENT_DEFAULT_MODEL '${defaultModel}' is not one of PORTARR_AGENT_MODELS.`);
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
    maxConversations: integer(env.PORTARR_AGENT_MAX_CONVERSATIONS, 200, 1, 5000),
    models,
    defaultModel,
    idleTimeoutMs: integer(env.PORTARR_AGENT_IDLE_MINUTES, 15, 1, 24 * 60) * 60_000,
    claudeCommand: env.CLAUDE_BIN ?? 'claude',
  };
}
