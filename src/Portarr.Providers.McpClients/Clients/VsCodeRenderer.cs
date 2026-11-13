using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Providers.McpClients;

/// <summary>
/// VS Code with GitHub Copilot: servers sit under <c>servers</c> in <c>mcp.json</c> (the user profile's, or <c>.vscode/mcp.json</c>
/// in a workspace), each with its <c>type</c>; <c>${env:VAR}</c> reads a variable. <c>code --add-mcp</c> adds to the user profile.
/// </summary>
public sealed class VsCodeRenderer : JsonConfigRenderer
{
    public override string Id => "vscode";

    public override string Name => "VS Code (GitHub Copilot)";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["servers"];

    protected override string? HttpType => "http";

    protected override string? StdioType => "stdio";

    protected override string FilePath(McpRenderContext context)
    {
        if (context.Scope == McpScopes.Project)
        {
            return $".vscode{context.Sep}mcp.json";
        }

        return context.Os switch
        {
            McpOperatingSystems.Windows => @"%APPDATA%\Code\User\mcp.json",
            McpOperatingSystems.MacOs => "~/Library/Application Support/Code/User/mcp.json",
            _ => "~/.config/Code/User/mcp.json",
        };
    }

    protected override string? Reference(string variable) => $"${{env:{variable}}}";

    protected override IEnumerable<McpOperation> InstallCommands(McpRenderContext context)
    {
        if (context.Scope != McpScopes.User)
        {
            yield break;
        }

        var entry = Entry(context);
        var ordered = new JsonObject { ["name"] = context.Server.Id };
        foreach (var (key, value) in entry.ToList())
        {
            entry.Remove(key);
            ordered[key] = value;
        }

        if (Quote(context, ordered.ToJsonString()) is { } quoted)
        {
            yield return new McpOperation(McpOperationKinds.Command, "Add the server to the user profile with the code command.", $"code --add-mcp {quoted}", null);
        }
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        yield return "The path of the user file is the usual one: \"MCP: Open User Configuration\" in VS Code opens the right file. VS Code asks to trust a server the first time it starts.";
    }
}
