using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Providers.McpClients;

/// <summary>
/// A convenience for the renderers of this library, not the contract (that is <see cref="IMcpClientRenderer"/>): it turns the two operations into
/// the template a renderer fills in, and holds a few helpers to write JSON the same way for every client (sorted keys, two-space indentation) and to
/// quote a text. A renderer says only what its client does differently; scope, operating system, variables and secrets are the Core's.
/// </summary>
public abstract class McpClientRendererBase : IMcpClientRenderer
{
    /// <summary>The characters that end a single-quoted string: the ASCII one, and the typographic ones PowerShell also reads as quotes (U+2018 to U+201B).</summary>
    private static readonly char[] SingleQuotes = ['\'', '\u2018', '\u2019', '\u201A', '\u201B'];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> Scopes { get; }

    public McpClientOperations Install(McpRenderContext context)
    {
        return new McpClientOperations(InstallOperations(context).ToList(), Notes(context).ToList());
    }

    public McpClientOperations Remove(McpRenderContext context)
    {
        return new McpClientOperations(RemoveOperations(context).ToList(), Notes(context).ToList());
    }

    protected abstract IEnumerable<McpOperation> InstallOperations(McpRenderContext context);

    protected abstract IEnumerable<McpOperation> RemoveOperations(McpRenderContext context);

    /// <summary>What the operator must know beyond the operations: a restart, a format that could not be verified.</summary>
    protected virtual IEnumerable<string> Notes(McpRenderContext context) => [];

    protected static string Json(JsonNode node) => node.ToJsonString(Indented);

    /// <summary>A file operation: merge this snippet into <paramref name="path"/>, every other entry kept.</summary>
    protected static McpOperation Merge(string path, string snippet, string what) =>
        new(McpOperationKinds.File, $"Merge this into {path}{what}; create the file if it does not exist and keep every other entry.", snippet, path);

    protected static McpOperation Delete(string path, string what) =>
        new(McpOperationKinds.Remove, $"If present, delete {what} from {path}; touch nothing else.", what, path);

    /// <summary>The headers of a remote server, in order, each value written by <paramref name="reference"/> when it is a variable.</summary>
    protected static JsonObject Pairs(IReadOnlyDictionary<string, McpValue> values, Func<string, string> reference)
    {
        var result = new JsonObject();
        foreach (var (name, value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            result[name] = value.IsEnv ? reference(value.EnvName!) : value.Literal;
        }

        return result;
    }

    protected static JsonArray Strings(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    /// <summary>A text between single quotes for a POSIX shell or PowerShell, or null when it holds a quote (the operator then uses the file).</summary>
    protected static string? Quote(string text) => text.IndexOfAny(SingleQuotes) >= 0 ? null : $"'{text}'";
}
