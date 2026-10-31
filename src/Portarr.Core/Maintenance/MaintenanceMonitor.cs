using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
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
    DateTimeOffset? GitHubAt,
    DateTimeOffset? GitHubNextAt,
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
    public static readonly TimeSpan RegistryInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RegistryRetryInterval = TimeSpan.FromHours(1);

    /// <summary>A manual refresh is accepted at most this often: GitHub's and the registries' limits matter more than impatience.</summary>
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Image repositories looked up per pass: a first start spreads its lookups over a few minutes.</summary>
    public const int RegistryLookupsPerPass = 25;

    /// <summary>Manifest reads per repository (digest of a floating tag, date of the newest tag).</summary>
    private const int DescribesPerRepository = 3;

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IEnumerable<ICodeRepositorySource> _codeSources;
    private readonly IEnumerable<IImageRegistry> _registries;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaintenanceMonitor> _logger;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Lock _requestLock = new();
    private volatile MaintenanceState _current = MaintenanceState.Empty;
    private GitHubSource? _scannedSource;

    // Manual refresh: when it was last accepted, and which request the last finished pass answered.
    private DateTimeOffset? _requestedAt;
    private DateTimeOffset? _answeredAt;
    private volatile TaskCompletionSource _wake = NewWake();

    public MaintenanceMonitor(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IEnumerable<ICodeRepositorySource> codeSources,
        IEnumerable<IImageRegistry> registries,
        TimeProvider timeProvider,
        ILogger<MaintenanceMonitor> logger)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _codeSources = codeSources;
        _registries = registries;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public MaintenanceState Current => _current;

    public MaintenanceRefresh Refresh
    {
        get
        {
            var state = _current;
            var githubAt = state.Repositories.At;
            var next = githubAt is { } at && _scannedSource is not null ? at + RepositoryInterval : (DateTimeOffset?)null;
            var registriesAt = state.Registries.Count == 0 ? (DateTimeOffset?)null : state.Registries.Values.Max(lookup => lookup.CheckedAt);
            lock (_requestLock)
            {
                var pending = _requestedAt is { } requested && (_answeredAt is null || _answeredAt < requested);
                var available = _requestedAt is { } last ? last + RefreshCooldown : (DateTimeOffset?)null;
                return new MaintenanceRefresh(githubAt, next, registriesAt, pending, available);
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

            var repositories = await ScanRepositoriesAsync(_current.Repositories, forced, cancellationToken);
            _current = _current with { Repositories = repositories };

            var registries = await LookUpImagesAsync(_current.Registries, forced, cancellationToken);
            _current = _current with { Registries = registries };

            lock (_requestLock)
            {
                _answeredAt = forced;
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
        var source = _catalogService.Current.Catalog.GitHub;
        var codeSource = _codeSources.FirstOrDefault();
        if (source is null || codeSource is null)
        {
            _scannedSource = null;
            return RepositoryScan.NotConfigured;
        }

        var now = _timeProvider.GetUtcNow();
        var sameSource = _scannedSource is not null && SameSource(_scannedSource, source);
        if (sameSource && previous.At is { } at && now - at < RepositoryInterval && !(forced is { } asked && at < asked))
        {
            return previous;
        }

        _scannedSource = source;
        if (codeSource.GetConfigurationError(source) is { } error)
        {
            return new RepositoryScan([], error, now);
        }

        try
        {
            var repositories = await codeSource.ScanAsync(source, cancellationToken);
            return new RepositoryScan(repositories, null, now);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogScanFailed(exception.Message);

            // Keep what was known: a failed scan must not empty the page; the error is shown next to it.
            return new RepositoryScan(sameSource ? previous.Repositories : [], ReasonCodes.From(exception), now);
        }
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

        var due = inUse
            .Where(group => !previous.TryGetValue(group.Key, out var lookup)
                || now - lookup.CheckedAt >= (lookup.Error is null ? RegistryInterval : RegistryRetryInterval)
                || lookup.CheckedAt < forced)
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

    private static bool SameSource(GitHubSource left, GitHubSource right)
    {
        static string Key(GitHubSource s) => string.Join('|', string.Join(',', s.Owners), s.TokenVariable, s.PushedWithinMonths, string.Join(',', s.Include), string.Join(',', s.Exclude), s.Forks, s.Archived);
        return Key(left) == Key(right);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance refresh failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Code repositories could not be scanned: {Reason}")]
    private partial void LogScanFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry lookup of {Repository} failed: {Reason}")]
    private partial void LogLookupFailed(string repository, string reason);
}
