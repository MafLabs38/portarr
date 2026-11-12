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

    /// <summary>What <c>cmd.exe</c> reads inside a command line, quotes or not: a text holding one is never written into a Windows command.</summary>
    private static readonly char[] CmdCharacters = ['"', '&', '|', '<', '>', '^', '%', '!', '\r', '\n'];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> Scopes { get; }

    public McpClientOperations Install(McpRenderContext context)
    {
        return Compose(context, InstallOperations(context).ToList());
    }

    public McpClientOperations Remove(McpRenderContext context)
    {
        return Compose(context, RemoveOperations(context).ToList());
    }

    protected abstract IEnumerable<McpOperation> InstallOperations(McpRenderContext context);

    protected abstract IEnumerable<McpOperation> RemoveOperations(McpRenderContext context);

    /// <summary>What the operator must know beyond the operations: a restart, a format that could not be verified.</summary>
    protected virtual IEnumerable<string> Notes(McpRenderContext context) => [];

    /// <summary>The operations, and the notes of the client; on Windows, the one that says where this library's quoting is understood.</summary>
    private McpClientOperations Compose(McpRenderContext context, List<McpOperation> operations)
    {
        var notes = Notes(context).ToList();
        if (context.Windows && operations.Exists(o => o.Kind == McpOperationKinds.Command))
        {
            notes.Add("Run the commands in PowerShell: they are quoted with single quotes, which cmd.exe does not read as quoting. A value with a character cmd.exe reads is left out of the command: use the file entry.");
        }

        return new McpClientOperations(operations, notes);
    }

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

    /// <summary>
    /// A text between single quotes for a POSIX shell or PowerShell, or null when it cannot be (the operator then uses the file): when it holds a quote
    /// and, on Windows, when it holds a character <c>cmd.exe</c> reads. PowerShell hands a new command line to a batch shim (<c>code.cmd</c>, the
    /// <c>.cmd</c> shims of the npm CLIs), and <c>cmd.exe</c> reads <c>&amp; | &lt; &gt; ^ % !</c> and double quotes whatever the quotes around them.
    /// </summary>
    protected static string? Quote(McpRenderContext context, string text)
    {
        if (text.IndexOfAny(SingleQuotes) >= 0 || (context.Windows && text.IndexOfAny(CmdCharacters) >= 0))
        {
            return null;
        }

        return $"'{text}'";
    }
}
