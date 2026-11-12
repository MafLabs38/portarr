using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Test.Common.Mcp;

/// <summary>An AI client for the tests of the Core: it renders one command that names the server and itself, and nothing else.</summary>
public sealed class StandInMcpClient : IMcpClientRenderer
{
    public StandInMcpClient(string id, params string[] scopes)
    {
        Id = id;
        Name = $"Client {id}";
        Scopes = scopes.Length > 0 ? scopes : [McpScopes.User, McpScopes.Project, McpScopes.Local];
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Scopes { get; }

    public McpClientOperations Install(McpRenderContext context) =>
        new([new McpOperation(McpOperationKinds.Command, "Stand-in install.", $"{Id} add {context.Server.Id} --scope {context.Scope} ({context.Os})")], [$"Note of {Id}."]);

    public McpClientOperations Remove(McpRenderContext context) =>
        new([new McpOperation(McpOperationKinds.Remove, $"If present, remove {context.Server.Id} from {Id}.", $"{Id} remove {context.Server.Id}")], []);
}

/// <summary>A kind of secret store for the tests of the Core: names without spaces or slashes, a provisioning line per secret.</summary>
public sealed class StandInSecretStore : IMcpSecretStoreProvisioner
{
    public StandInSecretStore(string kind)
    {
        Kind = kind;
    }

    public string Kind { get; }

    public string? CheckPrefix(string prefix) => prefix.Contains(' ', StringComparison.Ordinal) ? "must not contain spaces." : null;

    public string? CheckKey(string key) => key.Contains(' ', StringComparison.Ordinal) || key.Contains('/', StringComparison.Ordinal) ? "must be one name, never its value." : null;

    public string Identifier(McpSecretStore store, string key) => $"{store.Prefix}/{key}";

    public McpOperation Provision(McpSecretStore store, IReadOnlyList<McpSecretRef> secrets, string os) =>
        new(McpOperationKinds.Provision, "Stand-in provisioning.", string.Join('\n', secrets.Select(s => $"{s.Variable} <- {Identifier(store, s.Key)}")));

    public IReadOnlyList<string> Notes(McpSecretStore store, string os) => [$"Note of the {Kind} store."];
}

/// <summary>
/// The MCP adapters of the tests of the Core, as <see cref="TestCatalog"/> has its connectors: the nine client ids the catalog may name and two
/// kinds of store, rendered by stand-ins. The real adapters have their own tests, in their own library.
/// </summary>
public static class TestMcp
{
    public static IReadOnlyList<string> ClientIds { get; } =
        ["claude-code", "claude-desktop", "cursor", "vscode", "windsurf", "gemini-cli", "codex-cli", "opencode", "zed"];

    public static IMcpClientRegistry Clients { get; } = new McpClientRegistry(ClientIds.Select(id => id == "zed" ? new StandInMcpClient(id, McpScopes.User) : new StandInMcpClient(id)));

    public static IMcpSecretStoreRegistry Stores { get; } = new McpSecretStoreRegistry([new StandInSecretStore("vault"), new StandInSecretStore("azure-key-vault")]);

    public static IMcpVocabulary Vocabulary { get; } = new McpVocabulary(Clients, Stores);

    public static IMcpRenderingService Rendering(ICatalogService catalogService) => new McpRenderingService(Clients, Stores, catalogService);
}
