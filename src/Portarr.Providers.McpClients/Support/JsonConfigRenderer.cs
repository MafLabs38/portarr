using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Providers.McpClients;

/// <summary>
/// A client that keeps its servers in a JSON file: one entry per server under a root key. The differences between the clients
/// (the root key, the name of the address field, a <c>type</c>, the syntax that reads a variable, the file) are the members a
/// renderer overrides; a command the client provides is added on top.
/// </summary>
public abstract class JsonConfigRenderer : McpClientRendererBase
{
    /// <summary>The path of the object that holds the servers: <c>mcpServers</c>, or <c>mcp</c> then <c>servers</c>.</summary>
    protected abstract IReadOnlyList<string> RootPath(McpRenderContext context);

    /// <summary>The file that holds the servers at this scope, as the operating system writes it.</summary>
    protected abstract string FilePath(McpRenderContext context);

    /// <summary>How the client reads a variable inside the file, or null when it has no syntax (the operator then writes the value).</summary>
    protected abstract string? Reference(string variable);

    protected virtual string HttpUrlKey => "url";

    protected virtual string? HttpType => null;

    protected virtual string? StdioType => null;

    protected virtual IEnumerable<McpOperation> InstallCommands(McpRenderContext context) => [];

    protected virtual IEnumerable<McpOperation> RemoveCommands(McpRenderContext context) => [];

    /// <summary>A variable inside the file, or a placeholder naming the value the operator must write.</summary>
    protected string Ref(string variable) => Reference(variable) ?? $"<value of {variable}>";

    protected override IEnumerable<McpOperation> InstallOperations(McpRenderContext context)
    {
        foreach (var command in InstallCommands(context))
        {
            yield return command;
        }

        var snippet = new JsonObject { [context.Server.Id] = Entry(context) };
        foreach (var key in RootPath(context).Reverse())
        {
            snippet = new JsonObject { [key] = snippet };
        }

        yield return Merge(FilePath(context), Json(snippet), string.Empty);
    }

    protected override IEnumerable<McpOperation> RemoveOperations(McpRenderContext context)
    {
        foreach (var command in RemoveCommands(context))
        {
            yield return command;
        }

        yield return Delete(FilePath(context), $"the entry \"{context.Server.Id}\" under {string.Join(" > ", RootPath(context))}");
    }

    protected override IEnumerable<string> Notes(McpRenderContext context)
    {
        if (context.RequiredVariables().Count > 0 && Reference("X") is null)
        {
            yield return $"{Name} reads no variable inside its file: write the value of each <value of …> placeholder yourself, and keep that file private.";
        }
    }

    /// <summary>The entry of the server in the client's shape.</summary>
    protected virtual JsonObject Entry(McpRenderContext context)
    {
        var server = context.Server;
        var entry = new JsonObject();
        if (server.Http is { } http)
        {
            if (HttpType is not null)
            {
                entry["type"] = HttpType;
            }

            entry[HttpUrlKey] = http.Url.OriginalString;
            if (http.Headers.Count > 0)
            {
                entry["headers"] = Pairs(http.Headers, Ref);
            }

            return entry;
        }

        var stdio = server.Stdio!;
        if (StdioType is not null)
        {
            entry["type"] = StdioType;
        }

        entry["command"] = stdio.Command;
        if (stdio.Args.Count > 0)
        {
            entry["args"] = Strings(stdio.Args);
        }

        if (stdio.Env.Count > 0)
        {
            entry["env"] = Pairs(stdio.Env, Ref);
        }

        return entry;
    }

    /// <summary>A flag with a <c>name&lt;separator&gt;value</c> argument between single quotes, the value being the variable's syntax when it is one; null when the text holds a quote.</summary>
    protected string? Flag(string flag, string name, McpValue value, string separator)
    {
        var text = $"{name}{separator}{Text(value)}";
        return Quote(text) is { } quoted ? $"{flag} {quoted}" : null;
    }

    private string Text(McpValue value) => value.IsEnv ? Ref(value.EnvName!) : value.Literal!;

    /// <summary>
    /// A command-line argument: as is when it is plain, between single quotes otherwise, null when it holds a quote. A comma is not plain: in PowerShell's
    /// argument mode it is the array operator, so <c>a,b</c> would reach the program as two arguments.
    /// </summary>
    protected static string? Arg(string text)
    {
        return text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '@' or '%' or '+' or '=' or ':' or '.' or '/' or '-')
            ? text
            : Quote(text);
    }
}
