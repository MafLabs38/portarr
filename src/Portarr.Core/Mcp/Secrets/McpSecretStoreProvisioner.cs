using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Mcp.Secrets;

/// <summary>A secret a workstation needs: the variable the client reads, and the key of the secret in the store.</summary>
public sealed record McpSecretRef(string Variable, string Key);

/// <summary>
/// One provisioner per kind of secret store, behind one contract (rule 8, D-116). Given the secrets a rendering needs, it writes the command
/// the **workstation** runs in its own shell to put each secret's value in the variable the client reads: the value is read from the store at that
/// moment, under the store's access control, and never passes through Portarr, the assistant or its provider.
/// </summary>
public interface IMcpSecretStoreProvisioner
{
    /// <summary>The kind of store, as the catalog names it (<c>vault</c>, <c>azure-key-vault</c>…).</summary>
    string Kind { get; }

    /// <summary>
    /// Whether <paramref name="prefix"/>, the place under which the secrets live, suits this kind of store: null when it does, else the sentence the
    /// operator reads in the catalog's errors. The catalog parser asks, it knows nothing of any store.
    /// </summary>
    string? CheckPrefix(string prefix);

    /// <summary>Whether <paramref name="key"/>, the name of one secret under the prefix, suits this kind of store: null when it does, else the sentence.</summary>
    string? CheckKey(string key);

    /// <summary>What an operator grants access to in the store for this key: <c>kv/mcp/memory</c>, <c>myvault/mcp-memory</c>.</summary>
    string Identifier(McpSecretStore store, string key);

    /// <summary>The command that reads the secrets into their variables, for the operating system's shell (a POSIX shell, or PowerShell on Windows).</summary>
    McpOperation Provision(McpSecretStore store, IReadOnlyList<McpSecretRef> secrets, string os);

    /// <summary>What the operator must know: the store's command and login, where the variables live.</summary>
    IReadOnlyList<string> Notes(McpSecretStore store, string os);
}
