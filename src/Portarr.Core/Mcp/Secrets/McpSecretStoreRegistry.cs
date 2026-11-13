namespace Portarr.Core.Mcp.Secrets;

/// <summary>The kinds of secret store Portarr can write a provisioning command for (D-116). The adapters register themselves.</summary>
public interface IMcpSecretStoreRegistry
{
    IReadOnlyList<string> Kinds { get; }

    IMcpSecretStoreProvisioner? Find(string? kind);
}

public sealed class McpSecretStoreRegistry : IMcpSecretStoreRegistry
{
    private readonly IReadOnlyList<IMcpSecretStoreProvisioner> _provisioners;

    public McpSecretStoreRegistry(IEnumerable<IMcpSecretStoreProvisioner> provisioners)
    {
        _provisioners = provisioners.ToList();
        var duplicate = _provisioners.GroupBy(p => p.Kind, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two secret store provisioners claim the kind '{duplicate.Key}'.");
        }

        Kinds = _provisioners.Select(p => p.Kind).ToList();
    }

    public IReadOnlyList<string> Kinds { get; }

    public IMcpSecretStoreProvisioner? Find(string? kind) => _provisioners.FirstOrDefault(p => string.Equals(p.Kind, kind, StringComparison.Ordinal));
}
