using Portarr.Core.Attention;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Core.Mcp;

/// <summary>
/// The MCP servers that are Down, as one "what needs attention" finding (D-114): area <c>mcp</c>, kind <c>mcp-server-down</c>, each server with how
/// long it has been down. It is not part of the shared attention service, which every page and tool reads: only the tool that answers a caller who
/// holds <c>mcp.use</c> asks for it (D-115), so no other consumer has to remember to leave it out.
/// </summary>
public interface IMcpAttentionSource
{
    /// <summary>The finding for the servers that are Down, or null when none is.</summary>
    AttentionFinding? GetDownFinding();
}

public sealed class McpAttentionSource : IMcpAttentionSource
{
    public const string Area = "mcp";
    public const string DownKind = "mcp-server-down";

    private readonly ICatalogService _catalogService;
    private readonly IMcpHealth _health;
    private readonly TimeProvider _timeProvider;

    public McpAttentionSource(ICatalogService catalogService, IMcpHealth health, TimeProvider timeProvider)
    {
        _catalogService = catalogService;
        _health = health;
        _timeProvider = timeProvider;
    }

    public AttentionFinding? GetDownFinding()
    {
        var now = _timeProvider.GetUtcNow();
        var health = _health.Current;
        var down = _catalogService.Current.Catalog.McpServerList
            .Where(server => server.CheckInterval is not null)
            .Select(server => (Server: server, Health: health.GetValueOrDefault(server.Id)))
            .Where(x => x.Health is { State: McpHealthState.Down, CheckFailed: false })
            .OrderBy(x => x.Health!.Since)
            .Select(x => $"{x.Server.Name} (down for {Duration(now - x.Health!.Since)})")
            .ToList();
        return down.Count > 0 ? new AttentionFinding(Area, AttentionLevel.Warning, DownKind, down.Count, down) : null;
    }

    /// <summary>"12 d", "3 h", "5 min": the time a server has been down, as short as the list wants it.</summary>
    internal static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} d";
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h" : $"{Math.Max(1, (int)span.TotalMinutes)} min";
    }
}
