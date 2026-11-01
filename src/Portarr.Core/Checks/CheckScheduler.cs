using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Status;

namespace Portarr.Core.Checks;

/// <summary>
/// Runs every app check at its interval (D-50) and records the results (D-51). The app list is read
/// on every tick, so a catalog reload or a LAN URL derived from a new inventory is picked up quickly.
/// </summary>
public sealed partial class CheckScheduler : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    /// <summary>Checks probed at the same time: enough for a homelab, never a burst on one host.</summary>
    public const int MaxConcurrency = 6;

    private readonly IFleetService _fleetService;
    private readonly ICheckStore _store;
    private readonly IReadOnlyList<ICheckRunner> _runners;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckScheduler> _logger;

    // Only touched by RunDueAsync, which the loop never runs twice at the same time.
    private readonly Dictionary<string, DateTimeOffset> _nextRuns = new(StringComparer.Ordinal);

    public CheckScheduler(IFleetService fleetService, ICheckStore store, IEnumerable<ICheckRunner> runners, TimeProvider timeProvider, ILogger<CheckScheduler> logger)
    {
        _fleetService = fleetService;
        _store = store;
        _runners = runners.ToList();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Probes the checks that are due, at most <see cref="MaxConcurrency"/> at a time.</summary>
    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var checks = _fleetService.GetApps()
            .Where(app => app.Check is not null)
            .Select(app => (AppId: app.App.Id, Check: app.Check!))
            .ToList();

        var appIds = checks.Select(c => c.AppId).ToHashSet(StringComparer.Ordinal);
        _store.Retain(appIds.Select(id => new CheckId(id)).ToHashSet());
        foreach (var gone in _nextRuns.Keys.Where(id => !appIds.Contains(id)).ToList())
        {
            _nextRuns.Remove(gone);
        }

        // A changed check (new URL, new definition) has no state for it: it runs now.
        var due = checks.Where(c => c.Check.State is null || !_nextRuns.TryGetValue(c.AppId, out var next) || next <= now).ToList();
        foreach (var (appId, check) in due)
        {
            _nextRuns[appId] = now + check.Definition.Interval;
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(due, options, async (item, token) =>
        {
            var result = await ProbeAsync(item.Check, token);
            _store.Record(new CheckId(item.AppId), item.Check.Target, item.Check.Definition, result);
            if (!result.Up && result.Error != CheckErrors.CheckFailed)
            {
                LogCheckFailed(item.AppId, result.Error!);
            }
        });
    }

    /// <summary>
    /// One probe. The runner reports the failures it knows as a result; anything else it throws (a bug, an exception it does not map) is
    /// recorded as a failed check (Portarr's fault, not the app's), never allowed to cancel the other checks of the tick.
    /// </summary>
    private async Task<CheckResult> ProbeAsync(AppCheckView check, CancellationToken cancellationToken)
    {
        try
        {
            if (_runners.FirstOrDefault(runner => runner.Type == check.Definition.Type) is not { } runner)
            {
                LogNoRunner(check.Definition.Type);
                return new CheckResult(_timeProvider.GetUtcNow(), null, null, CheckErrors.CheckFailed, null);
            }

            return await runner.RunAsync(check.Target, check.Definition, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogProbeThrew(exception);
            return new CheckResult(_timeProvider.GetUtcNow(), null, null, CheckErrors.CheckFailed, null);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Check run failed")]
    private partial void LogTickFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No runner for the check type {Type}: the check is recorded as failed to run")]
    private partial void LogNoRunner(string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A check threw instead of reporting a result")]
    private partial void LogProbeThrew(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Check of {AppId} failed: {Reason}")]
    private partial void LogCheckFailed(string appId, string reason);
}
