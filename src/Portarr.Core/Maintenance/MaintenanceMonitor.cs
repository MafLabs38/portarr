using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Workloads;

namespace Portarr.Core.Maintenance;

/// <summary>The last scan of the code sources and the registry lookups, per repository key. In memory only.</summary>
public sealed record MaintenanceState(RepositoryScan Repositories, IReadOnlyDictionary<string, RegistryLookup> Registries)
{
    public static MaintenanceState Empty { get; } = new(RepositoryScan.NotConfigured, new Dictionary<string, RegistryLookup>());
}

/// <summary>
/// When the data was last read and when it is read next, and whether a manual refresh is under way.
/// <c>AvailableAt</c> is when the next manual refresh is accepted (null: now).
/// </summary>
public sealed record MaintenanceRefresh(
    DateTimeOffset? CodeAt,
    DateTimeOffset? CodeNextAt,
    DateTimeOffset? RegistriesAt,
    bool Refreshing,
    DateTimeOffset? AvailableAt)
{
    public static MaintenanceRefresh None { get; } = new(null, null, null, false, null);
}

/// <summary>The answer to a manual refresh: accepted, or refused until <c>RetryAt</c>.</summary>
public sealed record RefreshRequest(bool Accepted, DateTimeOffset RetryAt);

public interface IMaintenanceMonitor
{
    MaintenanceState Current { get; }

    /// <summary>When the data was read, when it is read next, whether a manual refresh is under way.</summary>
    MaintenanceRefresh Refresh { get; }

    /// <summary>
    /// Asks the monitor to rescan now, ignoring the cadences below (not GitHub's rate-limit guard, which the
    /// connector applies to every request). Accepted at most once per <see cref="MaintenanceMonitor.RefreshCooldown"/>.
    /// The scan runs in the background: this call returns at once.
    /// </summary>
    RefreshRequest RequestRefresh();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the code sources and the registries on their own slow cadence (D-64, D-66): repositories every
/// 30 minutes (conditional requests: an unchanged answer costs no rate limit), each image repository every
/// 6 hours (1 hour after a failure), new images within a minute. Respecting Docker Hub's and GitHub's limits
/// matters more than freshness: a release is news for days, not seconds.
/// </summary>
public sealed partial class MaintenanceMonitor : BackgroundService, IMaintenanceMonitor
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RepositoryInterval = TimeSpan.FromMinutes(30);

    /// <summary>Repositories are scanned again this soon after a failed scan, not after the 30 minutes of a good one.</summary>
    public static readonly TimeSpan RepositoryRetryInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RegistryInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RegistryRetryInterval = TimeSpan.FromHours(1);

    /// <summary>A manual refresh is accepted at most this often: GitHub's and the registries' limits matter more than impatience.</summary>
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(60);

    /// <summary>The reason an image has no verdict when the registry lookups are switched off.</summary>
    public const string DisabledReason = "disabled";

    /// <summary>Image repositories looked up per pass: a first start spreads its lookups over a few minutes.</summary>
    public const int RegistryLookupsPerPass = 25;

    /// <summary>Manifest reads per repository (digest of a floating tag, date of the newest tag).</summary>
    private const int DescribesPerRepository = 3;

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IEnumerable<ICodeRepositorySource> _codeSources;
    private readonly IEnumerable<IImageRegistry> _registries;
    private readonly bool _imageUpdates;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaintenanceMonitor> _logger;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Lock _requestLock = new();
    private volatile MaintenanceState _current = MaintenanceState.Empty;
    private volatile string? _scannedKey;

    // Manual refresh: when it was last accepted, and which request the last finished pass answered.
    private DateTimeOffset? _requestedAt;
    private DateTimeOffset? _answeredAt;
    private volatile TaskCompletionSource _wake = NewWake();

    public MaintenanceMonitor(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IEnumerable<ICodeRepositorySource> codeSources,
        IEnumerable<IImageRegistry> registries,
        IOptions<PortarrOptions> options,
        TimeProvider timeProvider,
        ILogger<MaintenanceMonitor> logger)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _codeSources = codeSources;
        _registries = registries;
        _imageUpdates = options.Value.ImageUpdates;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public MaintenanceState Current => _current;

    public MaintenanceRefresh Refresh
    {
        get
        {
            var state = _current;
            var codeAt = state.Repositories.At;
            var interval = state.Repositories.Error is null ? RepositoryInterval : RepositoryRetryInterval;
            var next = codeAt is { } at && _scannedKey is not null ? at + interval : (DateTimeOffset?)null;
            var registriesAt = state.Registries.Count == 0 ? (DateTimeOffset?)null : state.Registries.Values.Max(lookup => lookup.CheckedAt);
            lock (_requestLock)
            {
                var pending = _requestedAt is { } requested && (_answeredAt is null || _answeredAt < requested);
                var available = _requestedAt is { } last ? last + RefreshCooldown : (DateTimeOffset?)null;
                return new MaintenanceRefresh(codeAt, next, registriesAt, pending, available);
            }
        }
    }

    public RefreshRequest RequestRefresh()
    {
        var now = _timeProvider.GetUtcNow();
        TaskCompletionSource wake;
        lock (_requestLock)
        {
            if (_requestedAt is { } last && now - last < RefreshCooldown)
            {
                return new RefreshRequest(false, last + RefreshCooldown);
            }

            _requestedAt = now;
            wake = _wake;
        }

        wake.TrySetResult();
        return new RefreshRequest(true, now + RefreshCooldown);
    }

    /// <summary>The images a pass looks at: those of running Docker containers and Kubernetes pods.</summary>
    public static IEnumerable<(Workload Workload, ImageReference Image)> RunningImages(IReadOnlyDictionary<string, EnvironmentInventory> inventory)
    {
        return inventory.Values
            .SelectMany(environment => environment.Workloads)
            .Where(workload => workload.State is WorkloadState.Running or WorkloadState.Restarting or WorkloadState.Paused)
            .Select(workload => (Workload: workload, Image: ImageReference.Parse(workload.Image)))
            .Where(pair => pair.Image is not null)
            .Select(pair => (pair.Workload, pair.Image!));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshing.WaitAsync(cancellationToken);
        try
        {
            // A request made while this pass runs is answered by the next one.
            DateTimeOffset? forced;
            lock (_requestLock)
            {
                forced = _requestedAt;
            }

            try
            {
                var repositories = await ScanRepositoriesAsync(_current.Repositories, forced, cancellationToken);
                _current = _current with { Repositories = repositories };

                var registries = await LookUpImagesAsync(_current.Registries, forced, cancellationToken);
                _current = _current with { Registries = registries };
            }
            finally
            {
                // Answered even when the pass failed: the failure is logged and shown, and a refresh left "under way" forever
                // would keep the spinner on and the button disabled.
                lock (_requestLock)
                {
                    _answeredAt = forced;
                }
            }
        }
        finally
        {
            _refreshing.Release();
        }
    }

    public override void Dispose()
    {
        _refreshing.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _timeProvider);
        Task<bool>? tick = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogRefreshFailed(exception);
            }

            // One timer wait at a time: it is kept across wake-ups by a manual refresh.
            tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
            var woken = await Task.WhenAny(tick, _wake.Task);
            if (woken == tick)
            {
                if (!await tick)
                {
                    break;
                }

                tick = null;
            }
            else
            {
                lock (_requestLock)
                {
                    _wake = NewWake();
                }
            }
        }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<RepositoryScan> ScanRepositoriesAsync(RepositoryScan previous, DateTimeOffset? forced, CancellationToken cancellationToken)
    {
        // Each declared source goes to the connector that reads its kind; a source nothing reads is skipped.
        var sources = _catalogService.Current.Catalog.CodeSources
            .Select(source => (Source: source, Connector: _codeSources.FirstOrDefault(c => c.Supports(source))))
            .Where(pair => pair.Connector is not null)
            .Select(pair => (pair.Source, Connector: pair.Connector!))
            .ToList();
        if (sources.Count == 0)
        {
            _scannedKey = null;
            return RepositoryScan.NotConfigured;
        }

        var now = _timeProvider.GetUtcNow();
        var key = string.Join('\n', sources.Select(pair => pair.Source.Fingerprint));
        var sameSource = _scannedKey == key;
        if (sameSource && previous.At is { } at && now - at < (previous.Error is null ? RepositoryInterval : RepositoryRetryInterval) && !(forced is { } asked && at < asked))
        {
            return previous;
        }

        _scannedKey = key;
        var repositories = new List<CodeRepository>();
        var failedLogins = new List<string>();
        string? error = null;
        foreach (var (source, connector) in sources)
        {
            if (connector.GetConfigurationError(source) is { } configuration)
            {
                error ??= configuration;
                failedLogins.AddRange(source.Logins);
                continue;
            }

            try
            {
                repositories.AddRange(await connector.ScanAsync(source, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogScanFailed(exception.Message);
                error ??= ReasonCodes.From(exception);
                failedLogins.AddRange(source.Logins);
            }
        }

        // Keep what was known of a source that failed: a failed scan must not empty the page; the error is shown next to it.
        if (sameSource && failedLogins.Count > 0)
        {
            var seen = repositories.Select(r => r.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            repositories.AddRange(previous.Repositories.Where(r => failedLogins.Contains(r.Owner, StringComparer.OrdinalIgnoreCase) && seen.Add(r.FullName)));
        }

        return new RepositoryScan(repositories, error, now);
    }

    private async Task<IReadOnlyDictionary<string, RegistryLookup>> LookUpImagesAsync(IReadOnlyDictionary<string, RegistryLookup> previous, DateTimeOffset? forced, CancellationToken cancellationToken)
    {
        var registry = _registries.FirstOrDefault();
        if (registry is null)
        {
            return previous;
        }

        var now = _timeProvider.GetUtcNow();
        var inUse = RunningImages(_inventory.Current)
            .Where(pair => !pair.Image.IsImageId)
            .GroupBy(pair => pair.Image.RepositoryKey, StringComparer.Ordinal)
            .ToList();

        if (!_imageUpdates)
        {
            // Switched off (PORTARR__IMAGEUPDATES=false): no registry is asked, and each image says why it has no verdict.
            return inUse.ToDictionary(group => group.Key, _ => new RegistryLookup([], DisabledReason, now), StringComparer.Ordinal);
        }

        var due = inUse
            .Where(group => !previous.TryGetValue(group.Key, out var lookup)
                || now - lookup.CheckedAt >= (lookup.Error is null ? RegistryInterval : RegistryRetryInterval)
                || (forced is { } asked && lookup.CheckedAt < asked))
            .OrderBy(group => previous.ContainsKey(group.Key))
            .ThenBy(group => previous.TryGetValue(group.Key, out var lookup) ? lookup.CheckedAt : DateTimeOffset.MinValue)
            .Take(RegistryLookupsPerPass)
            .ToList();

        var results = new Dictionary<string, RegistryLookup>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken }, async (group, token) =>
        {
            var lookup = await LookUpAsync(registry, group.ToList(), token);
            lock (results)
            {
                results[group.Key] = lookup;
            }
        });

        // Repositories no longer in use are forgotten; the others keep their last lookup until it is due.
        var keys = inUse.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return previous.Where(entry => keys.Contains(entry.Key) && !results.ContainsKey(entry.Key))
            .Concat(results)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    private async Task<RegistryLookup> LookUpAsync(IImageRegistry registry, IReadOnlyList<(Workload Workload, ImageReference Image)> users, CancellationToken cancellationToken)
    {
        var image = users[0].Image;
        try
        {
            var tags = (await registry.ListTagsAsync(image, cancellationToken)).ToList();
            foreach (var tag in TagsToDescribe(users, tags).Take(DescribesPerRepository))
            {
                var described = await registry.DescribeAsync(image, tag, cancellationToken);
                var index = tags.FindIndex(t => t.Name == tag);
                if (described is not null && index >= 0)
                {
                    tags[index] = tags[index] with { Digest = tags[index].Digest ?? described.Digest, UpdatedAt = tags[index].UpdatedAt ?? described.UpdatedAt };
                }
            }

            return new RegistryLookup(tags, null, _timeProvider.GetUtcNow());
        }
        catch (RegistryUnavailableException unavailable)
        {
            return new RegistryLookup([], unavailable.Reason, _timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogLookupFailed(image.RepositoryKey, exception.Message);
            return new RegistryLookup([], ReasonCodes.From(exception), _timeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// Tags a list does not date or digest but the page needs: a floating tag whose running digest is known
    /// (to tell whether it moved), and the newest version of each tag in use (to tell how old the update is).
    /// </summary>
    private static IEnumerable<string> TagsToDescribe(IReadOnlyList<(Workload Workload, ImageReference Image)> users, List<RegistryTag> tags)
    {
        var byName = tags.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var wanted = new List<string>();
        foreach (var (workload, image) in users)
        {
            if (image.Tag is not { } tag)
            {
                continue;
            }

            if (VersionTag.Parse(tag) is { } version)
            {
                if (version.NewestIn(byName.Keys) is { } newest && byName[newest.Tag].UpdatedAt is null)
                {
                    wanted.Add(newest.Tag);
                }
            }
            else if (workload.ImageDigest is not null && byName.TryGetValue(tag, out var floating) && floating.Digest is null)
            {
                wanted.Add(tag);
            }
        }

        return wanted.Distinct(StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance refresh failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Code repositories could not be scanned: {Reason}")]
    private partial void LogScanFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry lookup of {Repository} failed: {Reason}")]
    private partial void LogLookupFailed(string repository, string reason);
}
