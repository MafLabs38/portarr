using Portarr.Core.Catalog;
namespace Portarr.Core.Mcp.Clients;

/// <summary>Zed: <c>context_servers</c> in the user's <c>settings.json</c>; no variable syntax is documented.</summary>
public sealed class ZedRenderer : JsonConfigRenderer
{
    public override string Id => "zed";

    public override string Name => "Zed";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["context_servers"];

    protected override string FilePath(McpRenderContext context)
    {
        return context.Windows ? @"%APPDATA%\Zed\settings.json" : "~/.config/zed/settings.json";
    }

    protected override string? Reference(string variable) => null;

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        foreach (var note in base.Notes(context))
        {
            yield return note;
        }

        yield return "Open the file with the action \"zed: open settings file\"; its location above is the usual one (not verified on every system).";
    }
}
