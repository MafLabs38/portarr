using Portarr.Core.Catalog;

namespace Portarr.Core.Mcp;

/// <summary>A server of the catalog with its health. <c>Status</c> is the word the pages and tools show.</summary>
public sealed record McpServerView(McpServerDefinition Server, McpServerHealth? Health)
{
    public const string HealthyStatus = "healthy";
    public const string AuthenticationRequiredStatus = "authentication-required";
    public const string UnknownStatus = "unknown";
    public const string DownStatus = "down";

    /// <summary>The server has a check but no result yet (just started, or just added).</summary>
    public const string WaitingStatus = "waiting";

    /// <summary>A local process, or <c>check: false</c>: nothing to say about it (D-114).</summary>
    public const string NotCheckedStatus = "not-checked";

    public string Status
    {
        get
        {
            if (Server.CheckInterval is null)
            {
                return NotCheckedStatus;
            }

            if (Health is { CheckFailed: true })
            {
                return UnknownStatus;
            }

            return Health?.State switch
            {
                McpHealthState.Healthy => HealthyStatus,
                McpHealthState.AuthenticationRequired => AuthenticationRequiredStatus,
                McpHealthState.Down => DownStatus,
                McpHealthState.Unknown => UnknownStatus,
                _ => WaitingStatus,
            };
        }
    }
}

public interface IMcpServerService
{
    /// <summary>The servers of the catalog, in its order, with their health.</summary>
    IReadOnlyList<McpServerView> List();

    /// <summary>A server by id or by name (case-insensitive), or null.</summary>
    McpServerView? Find(string idOrName);
}

public sealed class McpServerService : IMcpServerService
{
    private readonly ICatalogService _catalogService;
    private readonly IMcpHealth _health;

    public McpServerService(ICatalogService catalogService, IMcpHealth health)
    {
        _catalogService = catalogService;
        _health = health;
    }

    public IReadOnlyList<McpServerView> List()
    {
        var health = _health.Current;
        return _catalogService.Current.Catalog.McpServerList.Select(s => new McpServerView(s, health.GetValueOrDefault(s.Id))).ToList();
    }

    public McpServerView? Find(string idOrName)
    {
        var servers = List();
        return servers.FirstOrDefault(s => string.Equals(s.Server.Id, idOrName, StringComparison.OrdinalIgnoreCase))
            ?? servers.FirstOrDefault(s => string.Equals(s.Server.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }
}
