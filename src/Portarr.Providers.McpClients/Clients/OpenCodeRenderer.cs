using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Providers.McpClients;

/// <summary>
/// OpenCode: servers under <c>mcp.servers</c> (the current shape; older versions put them directly under <c>mcp</c>), a remote
/// one with <c>type: remote</c>, a local one with <c>type: local</c> and its command and arguments in one array; <c>{env:VAR}</c> reads a variable.
/// </summary>
public sealed class OpenCodeRenderer : JsonConfigRenderer
{
    public override string Id => "opencode";

    public override string Name => "OpenCode";

    public override IReadOnlyList<string> Scopes { get; } = [McpScopes.User, McpScopes.Project];

    protected override IReadOnlyList<string> RootPath(McpRenderContext context) => ["mcp", "servers"];

    protected override string FilePath(McpRenderContext context)
    {
        return context.Scope == McpScopes.Project ? "opencode.json" : $"{context.Home}{context.Sep}.config{context.Sep}opencode{context.Sep}opencode.json";
    }

    protected override string? Reference(string variable) => $"{{env:{variable}}}";

    protected override JsonObject Entry(McpRenderContext context)
    {
        var server = context.Server;
        if (server.Http is { } http)
        {
            var remote = new JsonObject { ["type"] = "remote", ["url"] = http.Url.OriginalString };
            if (http.Headers.Count > 0)
            {
                remote["headers"] = Pairs(http.Headers, Ref);
            }

            return remote;
        }

        var stdio = server.Stdio!;
        var local = new JsonObject { ["type"] = "local", ["command"] = Strings(new[] { stdio.Command }.Concat(stdio.Args)) };
        if (stdio.Env.Count > 0)
        {
            local["environment"] = Pairs(stdio.Env, Ref);
        }

        return local;
    }

    protected override IEnumerable<McpOperation> InstallCommands(McpRenderContext context)
    {
        var server = context.Server;
        var parts = new List<string?> { "opencode mcp add", server.Id };
        if (server.Http is { } http)
        {
            parts.Add(Arg(context, http.Url.OriginalString) is { } url ? $"--url {url}" : null);
            parts.AddRange(http.Headers.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => Flag(context, "--header", h.Key, h.Value, "=")));
        }
        else
        {
            parts.AddRange(server.Stdio!.Env.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => Flag(context, "--env", e.Key, e.Value, "=")));
        }

        if (context.Scope == McpScopes.User)
        {
            parts.Add("--global");
        }

        if (server.Stdio is { } local)
        {
            parts.Add("--");
            parts.Add(Arg(context, local.Command));
            parts.AddRange(local.Args.Select(a => Arg(context, a)));
        }

        if (!parts.Contains(null))
        {
            yield return new McpOperation(McpOperationKinds.Command, $"Add the server with OpenCode, at {context.Scope} scope.", string.Join(' ', parts), null);
        }
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        yield return "The file entry uses the current shape (mcp.servers). If your OpenCode is older, put the entry directly under mcp; the command above writes the right shape for the version you run.";
    }
}
