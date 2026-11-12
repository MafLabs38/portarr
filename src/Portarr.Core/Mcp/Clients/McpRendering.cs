using Portarr.Core.Catalog;
namespace Portarr.Core.Mcp.Clients;


/// <summary>What an assistant does to install or remove a server in a client (V9, D-113).</summary>
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

    /// <summary>The command the workstation runs to read the secrets from the operator's store into the variables the client reads (D-117).</summary>
    public const string Provision = "provision";
}

/// <summary>
/// The operations that install (or remove) one server in one client at one scope, for one operating system. <c>RequiresEnv</c>
/// lists the variables the workstation must provide, <c>RequiresSecrets</c> the keys, in the operator's secret store, the workstation must be able to read (D-117). <c>Supported</c> is false when the client cannot hold this server at this
/// scope; <c>Notes</c> then says why, and otherwise lists what the operator must know (a format not verified, a restart). Never a secret value.
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

/// <summary>One renderer per client (rule 8, D-113): the same contract, its own golden files.</summary>
public interface IMcpClientRenderer
{
    /// <summary>The client's id in the catalog: <c>claude-code</c>, <c>cursor</c>…</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>The scopes this client can hold a server at.</summary>
    IReadOnlyList<string> Scopes { get; }

    McpRendering Install(McpServerDefinition server, string scope, string os, McpSecretStore? secretStore = null);

    McpRendering Remove(McpServerDefinition server, string scope, string os);
}
