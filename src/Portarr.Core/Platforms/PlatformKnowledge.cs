using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Core.Platforms;

/// <summary>
/// What Portarr knows of the world's releases and support dates (D-119): the newest version of every release line per platform
/// (with the platform's display name), and the support cycles per product. A product no source lists is in <c>Unlisted</c>, and a lookup
/// that failed is in <c>Failures</c> with its reason code, so that "the sources do not know" and "the lookup failed" are never the same
/// silence. Keys are <c>lines:&lt;platform&gt;</c> and <c>cycles:&lt;product&gt;</c>. <c>Enabled</c> is false when the lookups are switched
/// off. In memory only: a restart reads again, the sources are cheap and public.
/// </summary>
public sealed record PlatformKnowledge(
    IReadOnlyDictionary<string, IReadOnlyList<ReleaseLine>> ReleaseLines,
    IReadOnlyDictionary<string, IReadOnlyList<SupportCycle>> Cycles,
    DateTimeOffset? CheckedAt,
    IReadOnlyDictionary<string, string>? DisplayNames = null,
    IReadOnlyDictionary<string, string>? Failures = null,
    IReadOnlySet<string>? Unlisted = null,
    bool Enabled = true)
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
/// lines of the distribution a node runs, a product's cycles when a machine runs it; at most <see cref="MaxReadsPerPass"/> products per
/// pass (the product names come from machines), and what no machine needs any more is forgotten. A failed lookup is kept with its
/// reason. Never writes anywhere; the sources are public.
/// </summary>
public sealed partial class PlatformMonitor : BackgroundService, IPlatformKnowledge
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>Support lookups per pass: a flood of product names from a rogue machine is spread over many passes, never sent at once.</summary>
    public const int MaxReadsPerPass = 32;

    private readonly IInfrastructureMonitor _infrastructure;
    private readonly IHostMetricsMonitor _hostMetrics;
    private readonly IReadOnlyList<IReleaseLineSource> _releaseSources;
    private readonly IReadOnlyList<ISupportLifecycleSource> _lifecycleSources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlatformMonitor> _logger;

    // Guarded by _gate: a pass runs alone.
    private readonly Dictionary<string, DateTimeOffset> _nextRuns = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _enabled;
    private volatile PlatformKnowledge _current;

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
        _current = PlatformKnowledge.Empty with { Enabled = _enabled };
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

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var wanted = PlatformReport.Wanted(PlatformReport.SystemsOf(_infrastructure.Current, _hostMetrics.Current));
        var pass = new Pass(_current);
        var changed = Forget(pass, wanted.ReleaseLines, wanted.Products);

        foreach (var platform in wanted.ReleaseLines.Where(p => IsDue($"lines:{p}", now)))
        {
            changed |= await ReadLinesAsync(platform, pass, now, cancellationToken);
        }

        foreach (var product in wanted.Products.Where(p => IsDue($"cycles:{p}", now)).Order(StringComparer.Ordinal).Take(MaxReadsPerPass))
        {
            changed |= await ReadCyclesAsync(product, pass, now, cancellationToken);
        }

        if (changed)
        {
            _current = pass.ToKnowledge(now);
        }
    }

    /// <summary>Drops what no machine needs any more: its answer, its failure and its schedule, so nothing grows with the names machines report.</summary>
    private bool Forget(Pass pass, IReadOnlySet<string> platforms, IReadOnlySet<string> products)
    {
        var keep = platforms.Select(p => $"lines:{p}").Concat(products.Select(p => $"cycles:{p}")).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var key in _nextRuns.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _nextRuns.Remove(key);
        }

        foreach (var platform in pass.Lines.Keys.Where(p => !platforms.Contains(p)).ToList())
        {
            pass.Lines.Remove(platform);
            pass.Names.Remove(platform);
            changed = true;
        }

        foreach (var product in pass.Cycles.Keys.Where(p => !products.Contains(p)).ToList())
        {
            pass.Cycles.Remove(product);
            changed = true;
        }

        foreach (var key in pass.Failures.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            pass.Failures.Remove(key);
            changed = true;
        }

        changed |= pass.Unlisted.RemoveWhere(k => !keep.Contains(k)) > 0;
        return changed;
    }

    private bool IsDue(string key, DateTimeOffset now) => !_nextRuns.TryGetValue(key, out var next) || next <= now;

    private async Task<bool> ReadLinesAsync(string platform, Pass pass, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = $"lines:{platform}";
        var source = _releaseSources.FirstOrDefault(s => s.Platform == platform);
        _nextRuns[key] = now + Interval;
        if (source is null)
        {
            // No source carries this platform: nothing to ask, and nothing failed.
            return false;
        }

        try
        {
            pass.Lines[platform] = await source.ReadAsync(cancellationToken);
            pass.Names[platform] = source.DisplayName;
            pass.Failures.Remove(key);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _nextRuns[key] = now + RetryInterval;
            pass.Failures[key] = ReasonCodes.From(exception);
            LogNotRead(platform, exception.GetType().Name);
            return true;
        }
    }

    private async Task<bool> ReadCyclesAsync(string product, Pass pass, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = $"cycles:{product}";
        string? failure = null;
        foreach (var source in _lifecycleSources)
        {
            try
            {
                if (await source.ReadAsync(product, cancellationToken) is { } read)
                {
                    pass.Cycles[product] = read;
                    pass.Failures.Remove(key);
                    pass.Unlisted.Remove(key);
                    _nextRuns[key] = now + Interval;
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failure = ReasonCodes.From(exception);
                LogNotRead(product, exception.GetType().Name);
            }
        }

        // Unknown to every source: asked again in a while, not at every pass, and said to be unlisted. A failure is retried sooner, keeps the last answer and is said.
        _nextRuns[key] = now + (failure is null ? Interval : RetryInterval);
        if (failure is null)
        {
            pass.Failures.Remove(key);
            return pass.Unlisted.Add(key);
        }

        pass.Failures[key] = failure;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The platform knowledge pass failed")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The releases or support dates of {Subject} could not be read ({Reason}): the last answer is kept")]
    private partial void LogNotRead(string subject, string reason);

    /// <summary>The knowledge as one pass changes it: copies of the dictionaries, turned back into a record at the end.</summary>
    private sealed class Pass
    {
        public Pass(PlatformKnowledge current)
        {
            Lines = current.ReleaseLines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Cycles = current.Cycles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Names = (current.DisplayNames ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Failures = (current.Failures ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Unlisted = new HashSet<string>(current.Unlisted ?? new HashSet<string>(), StringComparer.Ordinal);
            Enabled = current.Enabled;
        }

        public Dictionary<string, IReadOnlyList<ReleaseLine>> Lines { get; }

        public Dictionary<string, IReadOnlyList<SupportCycle>> Cycles { get; }

        public Dictionary<string, string> Names { get; }

        public Dictionary<string, string> Failures { get; }

        public HashSet<string> Unlisted { get; }

        public bool Enabled { get; }

        public PlatformKnowledge ToKnowledge(DateTimeOffset now) => new(Lines, Cycles, now, Names, Failures, Unlisted, Enabled);
    }
}
