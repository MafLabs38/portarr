using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;

namespace Portarr.Core.Mcp;

/// <summary>
/// Checks the remote MCP servers of the catalog at their interval (D-114) and keeps their health: the state, since when,
/// and when each was last seen healthy. The figures are loaded from the database at start and saved at every check, so
/// a restart does not forget that a server has been down for twelve days. Local (<c>stdio</c>) servers and servers with
/// <c>check: false</c> are not checked, and no workstation is ever probed. The catalog is read on every tick, so an edit is picked up quickly.
/// </summary>
public sealed partial class McpHealthMonitor : BackgroundService, IMcpHealth
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    /// <summary>Servers probed at the same time: a handful of servers, never a burst.</summary>
    public const int MaxConcurrency = 4;

    private readonly ICatalogService _catalogService;
    private readonly IMcpHealthStore _store;
    private readonly McpCheckRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McpHealthMonitor> _logger;
    private readonly Lock _lock = new();

    // Only touched by RunDueAsync, which the loop never runs twice at the same time.
    private readonly Dictionary<string, (DateTimeOffset Next, Uri Url)> _nextRuns = new(StringComparer.Ordinal);

    private volatile IReadOnlyDictionary<string, McpServerHealth> _current = new Dictionary<string, McpServerHealth>();

    public McpHealthMonitor(ICatalogService catalogService, IMcpHealthStore store, McpCheckRunner runner, TimeProvider timeProvider, ILogger<McpHealthMonitor> logger)
    {
        _catalogService = catalogService;
        _store = store;
        _runner = runner;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, McpServerHealth> Current => _current;

    /// <summary>Reads what the database remembers, so that "down since" and "last healthy" survive a restart.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _store.ReadAllAsync(cancellationToken);
            lock (_lock)
            {
                _current = stored.ToDictionary(h => h.ServerId, StringComparer.Ordinal);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotLoaded(exception.GetType().Name);
        }
    }

    /// <summary>Probes the servers that are due, at most <see cref="MaxConcurrency"/> at a time.</summary>
    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var state = _catalogService.Current;
        var servers = state.Catalog.McpServerList
            .Where(s => s is { IsHttp: true, Http: not null, CheckInterval: not null })
            .ToList();

        var ids = servers.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        // The history is only pruned against a catalog that is authoritative: one that was read and is valid. A file that is missing, unreadable or refused
        // (a typo, a key a release removed) leaves Portarr with no servers to compare with, and "down for twelve days" must survive the restart that fixes it (D-114).
        var authoritative = state is { FileExists: true, LoadedAt: not null, Errors.Count: 0 };
        if (authoritative && Forget(ids))
        {
            await RetainStoredAsync(ids, cancellationToken);
        }

        foreach (var gone in _nextRuns.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _nextRuns.Remove(gone);
        }

        // A new address starts a new life: it runs now.
        var due = servers.Where(s => !_nextRuns.TryGetValue(s.Id, out var next) || next.Url != s.Http!.Url || next.Next <= now).ToList();
        var moved = servers.Where(s => _nextRuns.TryGetValue(s.Id, out var known) && known.Url != s.Http!.Url).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        // Scheduled before the probe on purpose: a probe that is slow or cancelled must not be started again by the next tick. Memory only, so a restart probes everything.
        foreach (var server in due)
        {
            _nextRuns[server.Id] = (now + server.CheckInterval!.Value, server.Http!.Url);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(due, options, async (server, token) =>
        {
            var result = await ProbeAsync(server, token);
            // A server whose address changed since the last check is another server: what was known of the old address says nothing of the new one.
            var health = Next(server.Id, moved.Contains(server.Id) ? null : _current.GetValueOrDefault(server.Id), result);
            lock (_lock)
            {
                _current = new Dictionary<string, McpServerHealth>(_current, StringComparer.Ordinal) { [server.Id] = health };
            }

            if (!health.CheckFailed)
            {
                await SaveAsync(health, token);
            }
        });
    }

    /// <summary>
    /// The health after a check. The state is Down when the check failed, AuthenticationRequired for a 401 or 403, else Healthy;
    /// <c>Since</c> moves only when the server goes from up to down or back (healthy and "authentication required" are both up),
    /// <c>LastHealthyAt</c> only when it was up.
    /// </summary>
    internal static McpServerHealth Next(string serverId, McpServerHealth? previous, CheckResult result)
    {
        if (result.Error == CheckErrors.CheckFailed)
        {
            // D-104: a check that Portarr could not run says nothing about the server: what was known stays (the state, since when, the last healthy
            // time) and is shown as Unknown on top of it, until a check that ran says something.
            return previous is null
                ? new McpServerHealth(serverId, McpHealthState.Unknown, result.At, null, result.At, result.Error, null, CheckFailed: true)
                : previous with { LastCheckedAt = result.At, CheckFailed = true };
        }

        var up = result.Up;
        var state = McpHealthState.Down;
        if (up)
        {
            state = result.StatusCode is 401 or 403 ? McpHealthState.AuthenticationRequired : McpHealthState.Healthy;
        }

        var since = previous is not null && previous.State != McpHealthState.Unknown && previous.IsUp == up ? previous.Since : result.At;
        var lastHealthy = up ? result.At : previous?.LastHealthyAt;
        return new McpServerHealth(serverId, state, since, lastHealthy, result.At, result.Error, result.StatusCode);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadAsync(stoppingToken);

        using var timer = new PeriodicTimer(Tick, _timeProvider);
        do
        {
            try
            {
                await RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed tick must never end the service (and with it the host).
                LogTickFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<CheckResult> ProbeAsync(McpServerDefinition server, CancellationToken cancellationToken)
    {
        var definition = new CheckDefinition(server.Http!.Url, CheckDefinition.DefaultExpect, null, server.CheckInterval!.Value, server.CheckTimeout ?? CheckDefinition.DefaultTimeout, true, CheckTypes.Mcp);
        try
        {
            return await _runner.RunAsync(server.Http.Url, definition, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Portarr's own failure, never blamed on the server: recorded as a check that could not run.
            LogProbeThrew(exception);
            return new CheckResult(_timeProvider.GetUtcNow(), null, null, CheckErrors.CheckFailed, null);
        }
    }

    /// <summary>Drops from memory the servers that are no longer checked; true when there were some.</summary>
    private bool Forget(HashSet<string> ids)
    {
        lock (_lock)
        {
            if (!_current.Keys.Any(id => !ids.Contains(id)))
            {
                return false;
            }

            _current = _current.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return true;
        }
    }

    private async Task RetainStoredAsync(HashSet<string> ids, CancellationToken cancellationToken)
    {
        try
        {
            await _store.RetainAsync(ids, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotSaved(exception.GetType().Name);
        }
    }

    private async Task SaveAsync(McpServerHealth health, CancellationToken cancellationToken)
    {
        try
        {
            await _store.SaveAsync(health, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotSaved(exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP health run failed")]
    private partial void LogTickFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An MCP check threw instead of reporting a result")]
    private partial void LogProbeThrew(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The MCP health could not be read from the database ({Reason}): starting without it")]
    private partial void LogNotLoaded(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The MCP health could not be saved ({Reason}): kept in memory only")]
    private partial void LogNotSaved(string reason);
}
