using Portarr.Core.Catalog;
namespace Portarr.Core.Mcp.Clients;

/// <summary>Cursor: <c>~/.cursor/mcp.json</c> for the user, <c>.cursor/mcp.json</c> for a project; <c>${env:VAR}</c> reads a variable.</summary>
public sealed class CursorRenderer : JsonConfigRenderer
{
    public override string Id => "cursor";

    public override string Name => "Cursor";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["mcpServers"];

    protected override string FilePath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Project ? $".cursor{context.Sep}mcp.json" : $"{context.Home}{context.Sep}.cursor{context.Sep}mcp.json";
    }

    protected override string? Reference(string variable) => $"${{env:{variable}}}";
}
