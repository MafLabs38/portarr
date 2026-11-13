using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Core.Catalog;

/// <summary>
/// What the catalog parser needs to know about the adapters that registered (D-113, D-116), and nothing more: the ids of the AI clients a
/// workstation may name, the kinds of secret store, and whether a prefix or a key suits a kind. The parser holds no list of its own.
/// </summary>
public interface IMcpVocabulary
{
    IReadOnlyList<string> Clients { get; }

    IReadOnlyList<string> SecretStoreKinds { get; }

    /// <summary>The scopes a client holds a server at; empty for a client nobody registered.</summary>
    IReadOnlyList<string> ScopesOf(string client);

    /// <summary>Null when the prefix suits the kind of store, else the sentence for the operator.</summary>
    string? CheckSecretPrefix(string kind, string prefix);

    /// <summary>Null when the key suits the kind of store, else the sentence for the operator.</summary>
    string? CheckSecretKey(string kind, string key);
}

public sealed class McpVocabulary : IMcpVocabulary
{
    private readonly IMcpClientRegistry _clients;
    private readonly IMcpSecretStoreRegistry _stores;

    public McpVocabulary(IMcpClientRegistry clients, IMcpSecretStoreRegistry stores)
    {
        _clients = clients;
        Clients = clients.Ids;
        SecretStoreKinds = stores.Kinds;
        _stores = stores;
    }

    /// <summary>Nothing registered: any client or store kind named by a catalog is unknown. What a test of the generic parts of the catalog needs.</summary>
    public static IMcpVocabulary None { get; } = new McpVocabulary(new McpClientRegistry([]), new McpSecretStoreRegistry([]));

    public IReadOnlyList<string> Clients { get; }

    public IReadOnlyList<string> SecretStoreKinds { get; }

    public IReadOnlyList<string> ScopesOf(string client) => _clients.Find(client)?.Scopes ?? [];

    public string? CheckSecretPrefix(string kind, string prefix) => _stores.Find(kind)?.CheckPrefix(prefix);

    public string? CheckSecretKey(string kind, string key) => _stores.Find(kind)?.CheckKey(key);
}
