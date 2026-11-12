using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Core.Mcp.Clients;

/// <summary>What a renderer is asked: one server, at one scope, for one operating system.</summary>
public sealed record McpRenderContext(McpServerDefinition Server, string Scope, string Os)
{
    public bool Windows => Os == McpOperatingSystems.Windows;

    /// <summary>The user's home folder, as the operating system's shells write it.</summary>
    public string Home => Windows ? "%USERPROFILE%" : "~";

    public string Sep => Windows ? "\\" : "/";

    /// <summary>The secrets this server needs from the operator's store: the variable that receives each, and its key (D-117). One per variable.</summary>
    public IReadOnlyList<McpSecretRef> Secrets()
    {
        var values = (Server.Http?.Headers.Values ?? []).Concat(Server.Stdio?.Env.Values ?? []);
        return values.Where(v => v is { IsEnv: true, HasSecret: true })
            .Select(v => new McpSecretRef(v.EnvName!, v.SecretKey!))
            .DistinctBy(s => s.Variable)
            .OrderBy(s => s.Variable, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The variables the workstation must provide for this server, sorted.</summary>
    public IReadOnlyList<string> RequiredVariables()
    {
        var values = (Server.Http?.Headers.Values ?? []).Concat(Server.Stdio?.Env.Values ?? []);
        return values.Where(v => v.IsEnv).Select(v => v.EnvName!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// The shared part of the renderers: scope check, the variables a server needs, the notes, and a few helpers to write JSON
/// the same way for every client (sorted keys, two-space indentation). A renderer only says what its client does differently.
/// </summary>
public abstract class McpClientRenderer : IMcpClientRenderer
{
    /// <summary>The characters that end a single-quoted string: the ASCII one, and the typographic ones PowerShell also reads as quotes (U+2018 to U+201B).</summary>
    private static readonly char[] SingleQuotes = ['\'', '\u2018', '\u2019', '\u201A', '\u201B'];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> Scopes { get; }

    public McpRendering Install(McpServerDefinition server, string scope, string os, McpSecretStore? secretStore = null)
    {
        return Render(server, scope, os, removal: false, secretStore);
    }

    public McpRendering Remove(McpServerDefinition server, string scope, string os)
    {
        return Render(server, scope, os, removal: true, null);
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

    private McpRendering Render(McpServerDefinition server, string scope, string os, bool removal, McpSecretStore? secretStore)
    {
        if (!Scopes.Contains(scope))
        {
            return new McpRendering(server.Id, Id, Name, scope, false, [], [], [$"{Name} has no '{scope}' scope; it holds servers at: {string.Join(", ", Scopes)}."], []);
        }

        var context = new McpRenderContext(server, scope, os);
        var operations = (removal ? RemoveOperations(context) : InstallOperations(context)).ToList();
        var notes = Notes(context).ToList();
        if (context.Windows && operations.Exists(o => o.Kind == McpOperationKinds.Command))
        {
            notes.Add("Run the commands in PowerShell: they are quoted with single quotes, which cmd.exe does not read as quoting.");
        }

        if (!removal && context.Windows && server.Stdio?.Command is "npx" or "npx.cmd")
        {
            notes.Add("On native Windows, if the client cannot start npx, declare the command as cmd with the arguments /c npx … (not verified for every client).");
        }

        var secrets = removal || secretStore is null ? [] : context.Secrets();
        var requiresSecrets = new List<string>();
        if (secrets.Count > 0 && McpSecretStores.Find(secretStore!.Kind) is { } provisioner)
        {
            operations.Add(provisioner.Provision(secretStore, secrets, os));
            notes.AddRange(provisioner.Notes(secretStore, os));
            requiresSecrets.AddRange(secrets.Select(s => provisioner.Identifier(secretStore, s.Key)).Distinct(StringComparer.Ordinal));
        }

        return new McpRendering(server.Id, Id, Name, scope, true, removal ? [] : context.RequiredVariables(), operations, notes, requiresSecrets);
    }
}
