using System.Text;
using Portarr.Core.Catalog;

namespace Portarr.Core.Mcp.Clients;

/// <summary>
/// OpenAI Codex CLI keeps servers in TOML tables <c>[mcp_servers.&lt;name&gt;]</c> of <c>config.toml</c>. It has no <c>${VAR}</c>: a header
/// that comes from a variable is declared in <c>env_http_headers</c> (header to variable name) and a variable a local server needs
/// in <c>env_vars</c>, which forwards it unchanged.
/// </summary>
public sealed class CodexCliRenderer : McpClientRenderer
{
    public override string Id => "codex-cli";

    public override string Name => "Codex CLI";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IEnumerable<McpOperation> InstallOperations(McpRenderContext context)
    {
        yield return Merge(FilePath(context), Table(context.Server), string.Empty);
    }

    protected override IEnumerable<McpOperation> RemoveOperations(McpRenderContext context)
    {
        yield return Delete(FilePath(context), $"the table [mcp_servers.{context.Server.Id}] and its sub-tables");
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        if (context.Server.Stdio is { } stdio && stdio.Env.Any(e => e.Value.IsEnv && e.Value.EnvName != e.Key))
        {
            yield return "Codex CLI forwards a variable under its own name only: the variable of such an entry must be set under the name the server expects.";
        }

        yield return "A project's .codex/config.toml is read only for projects you have marked as trusted. The Windows location of the user file is the usual one (not verified).";
    }

    private static string FilePath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Project ? $".codex{context.Sep}config.toml" : $"{context.Home}{context.Sep}.codex{context.Sep}config.toml";
    }

    private static string Table(McpServerDefinition server)
    {
        var text = new StringBuilder();
        text.Append("[mcp_servers.").Append(server.Id).AppendLine("]");
        if (server.Http is { } http)
        {
            text.Append("url = ").AppendLine(Quoted(http.Url.OriginalString));
            Inline(text, "http_headers", http.Headers.Where(h => !h.Value.IsEnv).Select(h => (h.Key, Quoted(h.Value.Literal!))));
            Inline(text, "env_http_headers", http.Headers.Where(h => h.Value.IsEnv).Select(h => (h.Key, Quoted(h.Value.EnvName!))));
            return text.ToString().TrimEnd();
        }

        var stdio = server.Stdio!;
        text.Append("command = ").AppendLine(Quoted(stdio.Command));
        if (stdio.Args.Count > 0)
        {
            text.Append("args = [").AppendJoin(", ", stdio.Args.Select(Quoted)).AppendLine("]");
        }

        var forwarded = stdio.Env.Where(e => e.Value.IsEnv).Select(e => e.Value.EnvName!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (forwarded.Count > 0)
        {
            text.Append("env_vars = [").AppendJoin(", ", forwarded.Select(Quoted)).AppendLine("]");
        }

        var literals = stdio.Env.Where(e => !e.Value.IsEnv).OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        if (literals.Count > 0)
        {
            text.AppendLine().Append("[mcp_servers.").Append(server.Id).AppendLine(".env]");
            foreach (var (key, value) in literals)
            {
                text.Append(key).Append(" = ").AppendLine(Quoted(value.Literal!));
            }
        }

        return text.ToString().TrimEnd();
    }

    private static void Inline(StringBuilder text, string key, IEnumerable<(string Name, string Value)> pairs)
    {
        var ordered = pairs.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (ordered.Count > 0)
        {
            text.Append(key).Append(" = { ").AppendJoin(", ", ordered.Select(p => $"{Quoted(p.Name)} = {p.Value}")).AppendLine(" }");
        }
    }

    private static string Quoted(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
