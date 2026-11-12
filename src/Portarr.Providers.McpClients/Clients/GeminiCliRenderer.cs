using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
namespace Portarr.Providers.McpClients;

/// <summary>
/// Gemini CLI: <c>mcpServers</c> inside <c>settings.json</c> (<c>~/.gemini</c> for the user, <c>.gemini</c> for a project); a Streamable HTTP
/// server uses <c>httpUrl</c> (<c>url</c> means server-sent events); <c>${VAR}</c> reads a variable.
/// </summary>
public sealed class GeminiCliRenderer : JsonConfigRenderer
{
    public override string Id => "gemini-cli";

    public override string Name => "Gemini CLI";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["mcpServers"];

    protected override string HttpUrlKey => "httpUrl";

    protected override string FilePath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Project ? $".gemini{context.Sep}settings.json" : $"{context.Home}{context.Sep}.gemini{context.Sep}settings.json";
    }

    protected override string? Reference(string variable) => $"${{{variable}}}";

    protected override IEnumerable<McpOperation> InstallCommands(McpRenderContext context)
    {
        if (context.Server.Http is not { } http)
        {
            yield break;
        }

        var parts = new List<string?> { "gemini mcp add", $"--scope {context.Scope}", "--transport http" };
        parts.AddRange(http.Headers.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => Flag(context, "--header", h.Key, h.Value, ": ")));
        parts.Add(context.Server.Id);
        parts.Add(Arg(context, http.Url.OriginalString));
        if (!parts.Contains(null))
        {
            yield return new McpOperation(McpOperationKinds.Command, $"Add the server with Gemini CLI, at {context.Scope} scope.", string.Join(' ', parts), null);
        }
    }

    protected override IEnumerable<McpOperation> RemoveCommands(McpRenderContext context)
    {
        yield return new McpOperation(McpOperationKinds.Remove, $"If present, remove the server with Gemini CLI, at {context.Scope} scope.", $"gemini mcp remove --scope {context.Scope} {context.Server.Id}", null);
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        yield return "Avoid underscores in the server id: Gemini CLI splits tool names on the first one. A stdio server in an untrusted folder shows as disconnected until you run `gemini trust`.";
    }
}
