using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Infrastructure;

namespace Portarr.Core.Platforms;

/// <summary>
/// What Portarr knows of the world's releases and support dates (D-119): the newest version of every release line per platform
/// (with the platform's display name), and the support cycles per product. A product the lifecycle source does not know is absent. In memory only: a restart reads
/// again, the sources are cheap and public.
/// </summary>
public sealed record PlatformKnowledge(
    IReadOnlyDictionary<string, IReadOnlyList<ReleaseLine>> ReleaseLines,
    IReadOnlyDictionary<string, IReadOnlyList<SupportCycle>> Cycles,
    DateTimeOffset? CheckedAt,
    IReadOnlyDictionary<string, string>? DisplayNames = null)
{
    public static readonly PlatformKnowledge Empty = new(new Dictionary<string, IReadOnlyList<ReleaseLine>>(), new Dictionary<string, IReadOnlyList<SupportCycle>>(), null);
}

public interface IPlatformKnowledge
{
    PlatformKnowledge Current { get; }
}

/// <summary>
/// Reads, for the platforms the machines actually run, the release lines and the support cycles from the connectors that offer
/// them, every <see cref="Interval"/> (an hour after a failure, keeping the last good answer). Only what is needed is read: the
/// K3s lines when a node runs K3s, a product's cycles when a machine runs it. Never writes anywhere; the sources are public.
/// </summary>
public sealed partial class PlatformMonitor : BackgroundService, IPlatformKnowledge
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    private readonly IInfrastructureMonitor _infrastructure;
    private readonly IHostMetricsMonitor _hostMetrics;
    private readonly IReadOnlyList<IReleaseLineSource> _releaseSources;
    private readonly IReadOnlyList<ISupportLifecycleSource> _lifecycleSources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlatformMonitor> _logger;

    // Only touched by RunDueAsync, which the loop never runs twice at the same time.
    private readonly Dictionary<string, DateTimeOffset> _nextRuns = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _enabled;
    private volatile PlatformKnowledge _current = PlatformKnowledge.Empty;

    public PlatformMonitor(
        IInfrastructureMonitor infrastructure,
        IHostMetricsMonitor hostMetrics,
        IEnumerable<IReleaseLineSource> releaseSources,
        IEnumerable<ISupportLifecycleSource> lifecycleSources,
        IOptions<PortarrOptions> options,
        TimeProvider timeProvider,
        ILogger<PlatformMonitor> logger)
    {
        _enabled = options.Value.PlatformUpdates;
        _infrastructure = infrastructure;
        _hostMetrics = hostMetrics;
        _releaseSources = releaseSources.ToList();
        _lifecycleSources = lifecycleSources.ToList();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public PlatformKnowledge Current => _current;

    /// <summary>Reads what is due. Release lines are keyed <c>lines:&lt;platform&gt;</c>, cycles <c>cycles:&lt;product&gt;</c>.</summary>
    public async Task RunDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        // One pass at a time: the schedule is a plain dictionary, and a caller other than the loop (a test, a manual refresh) must not race it.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await RunPassAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var wanted = PlatformReport.Wanted(PlatformReport.SystemsOf(_infrastructure.Current, _hostMetrics.Current));
        var current = _current;
        var lines = current.ReleaseLines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var cycles = current.Cycles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var names = (current.DisplayNames ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var changed = false;

        foreach (var platform in wanted.ReleaseLines.Where(p => IsDue($"lines:{p}", now)))
        {
            changed |= await ReadLinesAsync(platform, lines, names, now, cancellationToken);
        }

        foreach (var product in wanted.Products.Where(p => IsDue($"cycles:{p}", now)))
        {
            changed |= await ReadCyclesAsync(product, cycles, now, cancellationToken);
        }

        if (changed)
        {
            _current = new PlatformKnowledge(lines, cycles, now, names);
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
                // A failed pass must never end the service (and with it the host).
                LogPassFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private bool IsDue(string key, DateTimeOffset now) => !_nextRuns.TryGetValue(key, out var next) || next <= now;

    private async Task<bool> ReadLinesAsync(string platform, Dictionary<string, IReadOnlyList<ReleaseLine>> lines, Dictionary<string, string> names, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var source = _releaseSources.FirstOrDefault(s => s.Platform == platform);
        if (source is null)
        {
            _nextRuns[$"lines:{platform}"] = now + Interval;
            return false;
        }

        try
        {
            lines[platform] = await source.ReadAsync(cancellationToken);
            names[platform] = source.DisplayName;
            _nextRuns[$"lines:{platform}"] = now + Interval;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _nextRuns[$"lines:{platform}"] = now + RetryInterval;
            LogNotRead(platform, exception.GetType().Name);
            return false;
        }
    }

    private async Task<bool> ReadCyclesAsync(string product, Dictionary<string, IReadOnlyList<SupportCycle>> cycles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = false;
        foreach (var source in _lifecycleSources)
        {
            try
            {
                if (await source.ReadAsync(product, cancellationToken) is { } read)
                {
                    cycles[product] = read;
                    _nextRuns[$"cycles:{product}"] = now + Interval;
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed = true;
                LogNotRead(product, exception.GetType().Name);
            }
        }

        // Unknown to every source: asked again in a while, not at every pass. A failure is retried sooner and keeps the last answer.
        _nextRuns[$"cycles:{product}"] = now + (failed ? RetryInterval : Interval);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The platform knowledge pass failed")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The releases or support dates of {Subject} could not be read ({Reason}): the last answer is kept")]
    private partial void LogNotRead(string subject, string reason);
}
