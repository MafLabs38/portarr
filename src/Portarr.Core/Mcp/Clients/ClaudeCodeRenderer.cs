using Portarr.Core.Catalog;
namespace Portarr.Core.Mcp.Clients;


/// <summary>
/// Claude Code (CLI). The user and local scopes live in <c>~/.claude.json</c> (local under the project's path), the project
/// scope in <c>.mcp.json</c> at the project's root; <c>${VAR}</c> is read from the environment. The command keeps the variables
/// between single quotes so the shell does not expand them into the file.
/// </summary>
public sealed class ClaudeCodeRenderer : JsonConfigRenderer
{
    public override string Id => "claude-code";

    public override string Name => "Claude Code";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project, McpScopes.Local];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Local ? ["projects", "<absolute path of the project>", "mcpServers"] : ["mcpServers"];
    }

    protected override string FilePath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Project ? ".mcp.json" : $"{context.Home}{context.Sep}.claude.json";
    }

    protected override string? Reference(string variable) => $"${{{variable}}}";

    protected override string? HttpType => "http";

    protected override IEnumerable<McpOperation> InstallCommands(McpRenderContext context)
    {
        var server = context.Server;
        var parts = new List<string?> { "claude mcp add", $"--transport {(server.IsHttp ? "http" : "stdio")}", $"--scope {context.Scope}" };
        if (server.Http is { } http)
        {
            parts.Add(server.Id);
            parts.Add(Arg(http.Url.OriginalString));
            parts.AddRange(http.Headers.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => Flag("--header", h.Key, h.Value, ": ")));
        }
        else
        {
            var stdio = server.Stdio!;
            parts.AddRange(stdio.Env.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => Flag("--env", e.Key, e.Value, "=")));
            parts.Add(server.Id);
            parts.Add("--");
            parts.Add(Arg(stdio.Command));
            parts.AddRange(stdio.Args.Select(Arg));
        }

        if (parts.Contains(null))
        {
            yield break;
        }

        yield return new McpOperation(McpOperationKinds.Command, $"Add the server with Claude Code, at {context.Scope} scope.", string.Join(' ', parts), null);
    }

    protected override IEnumerable<McpOperation> RemoveCommands(McpRenderContext context)
    {
        yield return new McpOperation(McpOperationKinds.Remove, $"If present, remove the server from Claude Code, at {context.Scope} scope.", $"claude mcp remove --scope {context.Scope} {context.Server.Id}", null);
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        foreach (var note in base.Notes(context))
        {
            yield return note;
        }

        if (context.Scope == McpScopes.Project)
        {
            yield return "A project server asks the user to approve it the first time Claude Code starts in that project.";
        }
    }
}
