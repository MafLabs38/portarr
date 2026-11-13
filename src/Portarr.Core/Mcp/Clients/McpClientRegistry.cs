namespace Portarr.Core.Mcp.Clients;

/// <summary>The AI clients Portarr can render a server for (D-113), by their id in the catalog. The adapters register themselves, like the connectors do.</summary>
public interface IMcpClientRegistry
{
    IReadOnlyList<IMcpClientRenderer> All { get; }

    IReadOnlyList<string> Ids { get; }

    IMcpClientRenderer? Find(string? id);
}

public sealed class McpClientRegistry : IMcpClientRegistry
{
    public McpClientRegistry(IEnumerable<IMcpClientRenderer> renderers)
    {
        All = renderers.ToList();
        var duplicate = All.GroupBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two MCP client renderers claim the id '{duplicate.Key}'.");
        }

        Ids = All.Select(r => r.Id).ToList();
    }

    public IReadOnlyList<IMcpClientRenderer> All { get; }

    public IReadOnlyList<string> Ids { get; }

    public IMcpClientRenderer? Find(string? id) => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
}
