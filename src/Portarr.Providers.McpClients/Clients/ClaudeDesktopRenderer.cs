using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Providers.McpClients;

/// <summary>
/// Claude Desktop keeps local servers in <c>claude_desktop_config.json</c> and documents no variable syntax inside it. A remote
/// server goes through the <c>mcp-remote</c> bridge (as Portarr's own page already shows), which reads the header value from
/// the <c>env</c> block of the entry: the operator writes that value.
/// </summary>
public sealed class ClaudeDesktopRenderer : JsonConfigRenderer
{
    /// <summary>The bridge Claude Desktop needs to reach a remote server, pinned: an unpinned package runs on every start (version available on 2026-11-12).</summary>
    private const string RemoteBridge = "mcp-remote@0.16.6";

    public override string Id => "claude-desktop";

    public override string Name => "Claude Desktop";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["mcpServers"];

    protected override string FilePath(McpRenderContext context)
    {
        return context.Os switch
        {
            McpOperatingSystems.Windows => @"%APPDATA%\Claude\claude_desktop_config.json",
            McpOperatingSystems.MacOs => "~/Library/Application Support/Claude/claude_desktop_config.json",
            _ => "~/.config/Claude/claude_desktop_config.json",
        };
    }

    protected override string? Reference(string variable) => null;

    protected override JsonObject Entry(McpRenderContext context)
    {
        if (context.Server.Http is not { } http)
        {
            return base.Entry(context);
        }

        var args = new List<string> { "-y", RemoteBridge, http.Url.OriginalString };
        var env = new JsonObject();
        foreach (var (name, value) in http.Headers.OrderBy(h => h.Key, StringComparer.Ordinal))
        {
            if (value.IsEnv)
            {
                args.Add("--header");
                args.Add($"{name}:${{{value.EnvName}}}");
                env[value.EnvName!] = Ref(value.EnvName!);
            }
            else
            {
                args.Add("--header");
                args.Add($"{name}:{value.Literal}");
            }
        }

        var entry = new JsonObject { ["command"] = "npx", ["args"] = Strings(args) };
        if (env.Count > 0)
        {
            entry["env"] = env;
        }

        return entry;
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        foreach (var note in base.Notes(context))
        {
            yield return note;
        }

        if (context.Server.IsHttp)
        {
            yield return "A remote server is reached through the mcp-remote bridge (Node.js required); Claude Desktop can also add it as a custom connector in Settings > Connectors.";
        }

        yield return "Quit Claude Desktop completely and start it again after editing the file.";
    }
}
