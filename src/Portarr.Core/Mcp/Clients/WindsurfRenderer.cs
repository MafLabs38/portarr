using Portarr.Core.Catalog;
namespace Portarr.Core.Mcp.Clients;

/// <summary>
/// Windsurf (its documentation now lives with Devin's): <c>mcp_config.json</c> under the <c>devin</c> configuration folder for the user,
/// <c>.devin/mcp_config.json</c> for a project; the address of a remote server is <c>serverUrl</c>; <c>${env:VAR}</c> reads a variable.
/// </summary>
public sealed class WindsurfRenderer : JsonConfigRenderer
{
    public override string Id => "windsurf";

    public override string Name => "Windsurf";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["mcpServers"];

    protected override string HttpUrlKey => "serverUrl";

    protected override string FilePath(McpRenderContext context)
    {
        if (context.Scope == McpScopes.Project)
        {
            return $".devin{context.Sep}mcp_config.json";
        }

        return context.Windows ? @"%AppData%\devin\mcp_config.json" : "~/.config/devin/mcp_config.json";
    }

    protected override string? Reference(string variable) => $"${{env:{variable}}}";

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        yield return "Older Windsurf builds read ~/.codeium/windsurf/mcp_config.json instead; use the file your installed version opens from its MCP settings.";
    }
}
