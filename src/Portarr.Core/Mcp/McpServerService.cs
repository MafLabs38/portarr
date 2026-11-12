using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Core.Mcp;

/// <summary>A server of the catalog with its health. <c>Status</c> is the word the pages and tools show.</summary>
public sealed record McpServerView(McpServerDefinition Server, McpServerHealth? Health)
{
    public const string HealthyStatus = "healthy";
    public const string AuthenticationRequiredStatus = "authentication-required";
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

            return Health?.State switch
            {
                McpHealthState.Healthy => HealthyStatus,
                McpHealthState.AuthenticationRequired => AuthenticationRequiredStatus,
                McpHealthState.Down => DownStatus,
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

    /// <summary>
    /// How to install the server in a client: the scope defaults to the server's, the operating system to Linux.
    /// Throws <see cref="ArgumentException"/> naming the valid values when the client, scope or system is unknown.
    /// </summary>
    McpRendering Render(McpServerDefinition server, string client, string? scope = null, string? os = null);

    /// <summary>How to remove the server from a client, if present; same defaults and errors as <see cref="Render"/>.</summary>
    McpRendering RenderRemoval(McpServerDefinition server, string client, string? scope = null, string? os = null);
}

public sealed class McpServerService : IMcpServerService
{
    public const string DefaultOs = McpOperatingSystems.Linux;

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

    public McpRendering Render(McpServerDefinition server, string client, string? scope = null, string? os = null)
    {
        var (renderer, resolvedScope, resolvedOs) = Resolve(server, client, scope, os);
        return renderer.Install(server, resolvedScope, resolvedOs, _catalogService.Current.Catalog.SecretStore);
    }

    public McpRendering RenderRemoval(McpServerDefinition server, string client, string? scope = null, string? os = null)
    {
        var (renderer, resolvedScope, resolvedOs) = Resolve(server, client, scope, os);
        return renderer.Remove(server, resolvedScope, resolvedOs);
    }

    private static (IMcpClientRenderer Renderer, string Scope, string Os) Resolve(McpServerDefinition server, string client, string? scope, string? os)
    {
        var renderer = McpClients.Find(client?.Trim().ToLowerInvariant())
            ?? throw new ArgumentException($"Unknown client '{client}'. Clients: {string.Join(", ", McpClients.Ids)}.", nameof(client));
        var resolvedScope = string.IsNullOrWhiteSpace(scope) ? server.Scope : scope.Trim().ToLowerInvariant();
        if (!McpScopes.All.Contains(resolvedScope))
        {
            throw new ArgumentException($"Unknown scope '{scope}'. Scopes: {string.Join(", ", McpScopes.All)}.", nameof(scope));
        }

        var resolvedOs = string.IsNullOrWhiteSpace(os) ? DefaultOs : os.Trim().ToLowerInvariant();
        if (!McpOperatingSystems.All.Contains(resolvedOs))
        {
            throw new ArgumentException($"Unknown operating system '{os}'. Systems: {string.Join(", ", McpOperatingSystems.All)}.", nameof(os));
        }

        return (renderer, resolvedScope, resolvedOs);
    }
}
