using Portarr.Core.Catalog;

namespace Portarr.Core.Mcp.Clients;

/// <summary>What an assistant does to install or remove a server in a client (D-113).</summary>
/// <param name="Kind">One of <see cref="McpOperationKinds"/>.</param>
/// <param name="Description">One sentence for the operator: what this does, where.</param>
/// <param name="Text">The command to run, or the snippet to merge into the file.</param>
/// <param name="Path">For a file operation: the file, as the operating system writes it.</param>
public sealed record McpOperation(string Kind, string Description, string Text, string? Path = null);

public static class McpOperationKinds
{
    /// <summary>A command the client provides (<c>claude mcp add …</c>).</summary>
    public const string Command = "command";

    /// <summary>An entry to merge into the client's configuration file, every other entry kept.</summary>
    public const string File = "file";

    /// <summary>Removal of an entry, if present.</summary>
    public const string Remove = "remove";

    /// <summary>The command the workstation runs to read the secrets from the operator's store into the variables the client reads (D-116).</summary>
    public const string Provision = "provision";
}

/// <summary>
/// The operations that install (or remove) one server in one client at one scope, for one operating system, as the Core composes them from the
/// client's own operations and what is generic. <c>RequiresEnv</c> lists the variables the workstation must provide, <c>RequiresSecrets</c> the
/// keys, in the operator's secret store, the workstation must be able to read (D-116). <c>Supported</c> is false when the client cannot hold this
/// server at this scope; <c>Notes</c> then says why, and otherwise lists what the operator must know (a format not verified, a restart). Never a
/// secret value.
/// </summary>
public sealed record McpRendering(
    string ServerId,
    string Client,
    string ClientName,
    string Scope,
    bool Supported,
    IReadOnlyList<string> RequiresEnv,
    IReadOnlyList<McpOperation> Operations,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> RequiresSecrets);

/// <summary>The operating systems a workstation runs: commands and paths differ.</summary>
public static class McpOperatingSystems
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string MacOs = "macos";

    public static IReadOnlyList<string> All { get; } = [Windows, Linux, MacOs];
}

/// <summary>What a client's renderer is asked: one server, at one scope the client supports, for one operating system.</summary>
public sealed record McpRenderContext(McpServerDefinition Server, string Scope, string Os)
{
    public bool Windows => Os == McpOperatingSystems.Windows;

    /// <summary>The user's home folder, as the operating system's shells write it.</summary>
    public string Home => Windows ? "%USERPROFILE%" : "~";

    /// <summary>The separator of a path, as the operating system writes it.</summary>
    public string Sep => Windows ? "\\" : "/";

    /// <summary>The variables the workstation must provide for this server (see <see cref="McpServerDefinition.RequiredVariables"/>).</summary>
    public IReadOnlyList<string> RequiredVariables() => Server.RequiredVariables();
}

/// <summary>What is specific to a client in an installation or a removal: its operations, and what the operator must know about this client.</summary>
public sealed record McpClientOperations(IReadOnlyList<McpOperation> Operations, IReadOnlyList<string> Notes);

/// <summary>
/// The adapter of one AI client (rule 8, D-113). It turns a server into what that client needs, and nothing else: it knows nothing of the
/// other clients, of the secret store, of how a scope is chosen or an operating system defaulted. The Core composes those
/// (<see cref="IMcpRenderingService"/>), so any library can add a client by implementing this and registering it.
/// </summary>
public interface IMcpClientRenderer
{
    /// <summary>The client's id in the catalog: <c>claude-code</c>, <c>cursor</c>…</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>The scopes this client can hold a server at.</summary>
    IReadOnlyList<string> Scopes { get; }

    /// <summary>How to add the server to the client. Called only for a scope the client supports.</summary>
    McpClientOperations Install(McpRenderContext context);

    /// <summary>How to remove the server from the client, if present. Called only for a scope the client supports.</summary>
    McpClientOperations Remove(McpRenderContext context);
}
