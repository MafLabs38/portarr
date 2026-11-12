using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Mcp.Secrets;

/// <summary>A secret a workstation needs: the variable the client reads, and the key of the secret in the store.</summary>
public sealed record McpSecretRef(string Variable, string Key);

/// <summary>
/// One provisioner per kind of secret store, behind one contract (rule 8, D-117). Given the secrets a rendering needs, it writes the command
/// the **workstation** runs in its own shell to put each secret's value in the variable the client reads: the value is read from the store at that
/// moment, under the store's access control, and never passes through Portarr, the assistant or its provider.
/// </summary>
public interface IMcpSecretStoreProvisioner
{
    string Kind { get; }

    /// <summary>What an operator grants access to in the store for this key: <c>kv/mcp/memory</c>, <c>myvault/mcp-memory</c>.</summary>
    string Identifier(McpSecretStore store, string key);

    /// <summary>The command that reads the secrets into their variables, for the operating system's shell (a POSIX shell, or PowerShell on Windows).</summary>
    McpOperation Provision(McpSecretStore store, IReadOnlyList<McpSecretRef> secrets, string os);

    /// <summary>What the operator must know: the store's command and login, where the variables live.</summary>
    IReadOnlyList<string> Notes(McpSecretStore store, string os);
}

/// <summary>HashiCorp Vault: <c>vault kv get -field=value &lt;prefix&gt;/&lt;key&gt;</c>; each secret keeps its value in the field <c>value</c>.</summary>
public sealed class VaultProvisioner : IMcpSecretStoreProvisioner
{
    public string Kind => McpSecretStoreKinds.Vault;

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

/// <summary>Azure Key Vault: <c>az keyvault secret show</c>; a secret's name is the store's prefix followed by the key.</summary>
public sealed class AzureKeyVaultProvisioner : IMcpSecretStoreProvisioner
{
    public string Kind => McpSecretStoreKinds.AzureKeyVault;

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

/// <summary>The kinds of secret store Portarr can write provisioning commands for (D-117).</summary>
public static class McpSecretStores
{
    public static IReadOnlyList<IMcpSecretStoreProvisioner> All { get; } = [new VaultProvisioner(), new AzureKeyVaultProvisioner()];

    public static IMcpSecretStoreProvisioner? Find(string kind) => All.FirstOrDefault(p => p.Kind == kind);
}
