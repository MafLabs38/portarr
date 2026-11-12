using Portarr.Core.Attention;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Core.Mcp;

/// <summary>
/// Adds the MCP servers that are Down to "what needs attention" (D-114): finding <c>mcp-server-down</c>, area <c>mcp</c>,
/// each server with how long it has been down. A decorator of the cross-domain service, so the other areas stay as they are.
/// The MCP tools show the area only to callers who hold <c>mcp.use</c>.
/// </summary>
public sealed class McpAttentionService : IAttentionService
{
    public const string Area = "mcp";
    public const string DownKind = "mcp-server-down";

    private readonly IAttentionService _inner;
    private readonly ICatalogService _catalogService;
    private readonly IMcpHealth _health;
    private readonly TimeProvider _timeProvider;

    public McpAttentionService(IAttentionService inner, ICatalogService catalogService, IMcpHealth health, TimeProvider timeProvider)
    {
        _inner = inner;
        _catalogService = catalogService;
        _health = health;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<AttentionFinding> GetFindings()
    {
        var findings = _inner.GetFindings().ToList();
        var now = _timeProvider.GetUtcNow();
        var health = _health.Current;
        var down = _catalogService.Current.Catalog.McpServerList
            .Select(server => (Server: server, Health: health.GetValueOrDefault(server.Id)))
            .Where(x => x.Health is { State: McpHealthState.Down, CheckFailed: false })
            .OrderBy(x => x.Health!.Since)
            .Select(x => $"{x.Server.Name} (down for {Duration(now - x.Health!.Since)})")
            .ToList();
        if (down.Count > 0)
        {
            findings.Add(new AttentionFinding(Area, AttentionLevel.Warning, DownKind, down.Count, down));
        }

        return findings.OrderBy(f => f.Level).ToList();
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
