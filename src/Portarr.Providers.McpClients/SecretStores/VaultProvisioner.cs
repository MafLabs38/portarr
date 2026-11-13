using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Providers.McpClients;

/// <summary>HashiCorp Vault: <c>vault kv get -field=value &lt;prefix&gt;/&lt;key&gt;</c>; each secret keeps its value in the field <c>value</c>.</summary>
public sealed partial class VaultProvisioner : IMcpSecretStoreProvisioner
{
    public const string StoreKind = "vault";

    public string Kind => StoreKind;

    public string? CheckPrefix(string prefix) => PrefixPattern().IsMatch(prefix)
        ? null
        : "must be the path under which the MCP secrets live, such as kv/mcp (letters, digits, slashes, dots, dashes and underscores; no slash at either end).";

    public string? CheckKey(string key) => KeyPattern().IsMatch(key)
        ? null
        : "must be the key of the secret under the store's prefix (letters, digits, dots, dashes and underscores), never its value.";

    /// <summary>The path in Vault under which the MCP secrets live: <c>kv/mcp</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*\z")]
    private static partial Regex PrefixPattern();

    /// <summary>The key of a secret in a Vault-like store: one name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z")]
    private static partial Regex KeyPattern();

    public string Identifier(McpSecretStore store, string key) => $"{store.Prefix}/{key}";

    public McpOperation Provision(McpSecretStore store, IReadOnlyList<McpSecretRef> secrets, string os)
    {
        var windows = os == McpOperatingSystems.Windows;
        var address = store.Address.OriginalString.TrimEnd('/');
        var lines = new List<string> { windows ? $"$env:VAULT_ADDR = '{address}'" : $"export VAULT_ADDR='{address}'" };
        foreach (var secret in secrets)
        {
            var read = $"vault kv get -field=value {Identifier(store, secret.Key)}";
            lines.Add(windows ? $"$env:{secret.Variable} = ({read})" : $"export {secret.Variable}=\"$({read})\"");
        }

        return new McpOperation(
            McpOperationKinds.Provision,
            windows ? "In PowerShell: read the secrets from the vault into the variables the client reads." : "In your shell profile: read the secrets from the vault into the variables the client reads.",
            string.Join('\n', lines));
    }

    public IReadOnlyList<string> Notes(McpSecretStore store, string os)
    {
        var notes = new List<string>
        {
            "Needs the vault CLI and a login (vault login) whose policy may read these keys; each secret keeps its value in the field named value. The value is read each time, and written to no file.",
        };
        notes.Add(os == McpOperatingSystems.Windows
            ? "A variable set this way lives in this PowerShell session: start the client from it. Setting it for your user would store the value on this machine."
            : "A program started from a launcher rather than a shell does not see these variables: start the client from a shell.");
        return notes;
    }
}
