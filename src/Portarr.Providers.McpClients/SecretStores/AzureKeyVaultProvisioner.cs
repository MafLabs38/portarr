using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;

namespace Portarr.Providers.McpClients;

/// <summary>Azure Key Vault: <c>az keyvault secret show</c>; a secret's name is the store's prefix followed by the key.</summary>
public sealed partial class AzureKeyVaultProvisioner : IMcpSecretStoreProvisioner
{
    public const string StoreKind = "azure-key-vault";

    public string Kind => StoreKind;

    public string? CheckPrefix(string prefix) => PrefixPattern().IsMatch(prefix)
        ? null
        : "is put before the key to make the secret's name: at most 50 letters, digits and dashes (mcp-), or left out.";

    public string? CheckKey(string key) => NamePattern().IsMatch(key)
        ? null
        : "must be the key of the secret under the store's prefix (letters, digits and dashes), never its value.";

    /// <summary>What an Azure Key Vault secret's name starts with: <c>mcp-</c>.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{0,50}\\z")]
    private static partial Regex PrefixPattern();

    /// <summary>The name of an Azure Key Vault secret: letters, digits and dashes.</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{1,63}\\z")]
    private static partial Regex NamePattern();

    public string Identifier(McpSecretStore store, string key) => $"{VaultName(store)}/{SecretName(store, key)}";

    public McpOperation Provision(McpSecretStore store, IReadOnlyList<McpSecretRef> secrets, string os)
    {
        var windows = os == McpOperatingSystems.Windows;
        var lines = secrets.Select(secret =>
        {
            var read = $"az keyvault secret show --vault-name {VaultName(store)} --name {SecretName(store, secret.Key)} --query value -o tsv";
            return windows ? $"$env:{secret.Variable} = {read}" : $"export {secret.Variable}=\"$({read})\"";
        });

        return new McpOperation(
            McpOperationKinds.Provision,
            windows ? "In PowerShell: read the secrets from the key vault into the variables the client reads." : "In your shell profile: read the secrets from the key vault into the variables the client reads.",
            string.Join('\n', lines));
    }

    public IReadOnlyList<string> Notes(McpSecretStore store, string os)
    {
        var notes = new List<string>
        {
            "Needs the Azure CLI, signed in (az login), with a role that reads secrets on this vault (Key Vault Secrets User). The value is read each time, and written to no file.",
        };
        notes.Add(os == McpOperatingSystems.Windows
            ? "A variable set this way lives in this PowerShell session: start the client from it. Setting it for your user would store the value on this machine."
            : "A program started from a launcher rather than a shell does not see these variables: start the client from a shell.");
        return notes;
    }

    /// <summary>The vault's name: the first label of its address (<c>myvault</c> of <c>https://myvault.vault.azure.net</c>).</summary>
    private static string VaultName(McpSecretStore store) => store.Address.Host.Split('.')[0];

    private static string SecretName(McpSecretStore store, string key) => store.Prefix + key;
}
