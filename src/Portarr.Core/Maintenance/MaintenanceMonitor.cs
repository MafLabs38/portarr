using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Workloads;

namespace Portarr.Core.Maintenance;

/// <summary>The last scan of the code sources, the registry lookups per repository key, the image scanner's report and the exploit intelligence (D-133). In memory only.</summary>
public sealed record MaintenanceState(RepositoryScan Repositories, IReadOnlyDictionary<string, RegistryLookup> Registries, ImageScanSnapshot? ImageScans = null, ThreatIntelSnapshot? ThreatIntel = null)
{
    public static MaintenanceState Empty { get; } = new(RepositoryScan.NotConfigured, new Dictionary<string, RegistryLookup>());

    /// <summary>What the image scanner said at the last read (<see cref="ImageScanSnapshot.Off"/> when none is declared).</summary>
    public ImageScanSnapshot Scans => ImageScans ?? ImageScanSnapshot.Off;

    /// <summary>What the exploit intelligence said at the last read (<see cref="ThreatIntelSnapshot.Off"/> when it is not read).</summary>
    public ThreatIntelSnapshot Intel => ThreatIntel ?? ThreatIntelSnapshot.Off;
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

    /// <summary>The image scanner is told the running images and read at most this often (it scans each image once a day on its own).</summary>
    public static readonly TimeSpan ScannerInterval = TimeSpan.FromMinutes(30);

    /// <summary>The image scanner is asked again this soon after a failure.</summary>
    public static readonly TimeSpan ScannerRetryInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The exploit intelligence (D-133) is read once a day: its sources publish once a day. A manual refresh does not read it again (the files
    /// are large, and the next version is a day away).
    /// </summary>
    public static readonly TimeSpan ThreatIntelInterval = TimeSpan.FromHours(24);

    /// <summary>The exploit intelligence is read again this soon after a failure.</summary>
    public static readonly TimeSpan ThreatIntelRetryInterval = TimeSpan.FromHours(1);

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
    private readonly IEnumerable<IImageScanner> _scanners;
    private readonly IEnumerable<IExploitIntelligence> _exploitIntelligence;
    private readonly bool _imageUpdates;
    private readonly bool _threatIntel;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaintenanceMonitor> _logger;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Lock _requestLock = new();
    private volatile MaintenanceState _current = MaintenanceState.Empty;
    private volatile string? _scannedKey;
    private volatile string? _scannerKey;

    // Whether the last read of the exploit intelligence had no CVE id to ask about (a first start, before the scanner's first answer).
    private volatile bool _threatIntelAskedNone;

    // Manual refresh: when it was last accepted, and which request the last finished pass answered.
    private DateTimeOffset? _requestedAt;
    private DateTimeOffset? _answeredAt;
    private volatile TaskCompletionSource _wake = NewWake();

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Dependency injection: each parameter is a connector capability or a service the monitor orchestrates; a parameter object would only hide them.")]
    public MaintenanceMonitor(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IEnumerable<ICodeRepositorySource> codeSources,
        IEnumerable<IImageRegistry> registries,
        IEnumerable<IImageScanner> scanners,
        IEnumerable<IExploitIntelligence> exploitIntelligence,
        IOptions<PortarrOptions> options,
        TimeProvider timeProvider,
        ILogger<MaintenanceMonitor> logger)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _codeSources = codeSources;
        _registries = registries;
        _scanners = scanners;
        _exploitIntelligence = exploitIntelligence;
        _imageUpdates = options.Value.ImageUpdates;
        _threatIntel = options.Value.ThreatIntel.Enabled;
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

                var scans = await ReadScannerAsync(_current.Scans, forced, cancellationToken);
                _current = _current with { ImageScans = scans };

                var intel = await ReadThreatIntelAsync(_current.Intel, scans, cancellationToken);
                _current = _current with { ThreatIntel = intel };
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

    /// <summary>
    /// The image scanner (D-131), when the catalog declares one: told the images that run and the repositories whose latest release is scanned (D-132), and read, at most every 30 minutes (5 after a
    /// failure). A scanner that cannot be reached keeps the previous results, with the error next to them; one that is not declared is off.
    /// </summary>
    private async Task<ImageScanSnapshot> ReadScannerAsync(ImageScanSnapshot previous, DateTimeOffset? forced, CancellationToken cancellationToken)
    {
        var source = _catalogService.Current.Catalog.ImageScanner;
        var scanner = _scanners.FirstOrDefault();
        if (source is null || scanner is null)
        {
            _scannerKey = null;
            return ImageScanSnapshot.Off;
        }

        var now = _timeProvider.GetUtcNow();
        var sameSource = _scannerKey == source.Fingerprint;
        if (sameSource && previous.At is { } at && now - at < (previous.Error is null ? ScannerInterval : ScannerRetryInterval) && !(forced is { } asked && at < asked))
        {
            return previous;
        }

        _scannerKey = source.Fingerprint;
        // Another scanner (its address changed) knows nothing of what the previous one said.
        var kept = sameSource ? previous : ImageScanSnapshot.Off;
        var targets = RunningImages(_inventory.Current)
            .Where(pair => !pair.Image.IsImageId)
            .Select(pair => pair.Image.ScanReference)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(image => new ScanTarget(ScanTargetKinds.Image, image))
            .Concat(ReleaseTargets.Of(_catalogService.Current.Catalog).ScanTargets(_current.Repositories.Repositories))
            .ToList();
        try
        {
            await RequestScansAsync(scanner, targets, cancellationToken);
            var report = await scanner.ReadAsync(cancellationToken);
            return new ImageScanSnapshot(
                report.Images.Where(i => i.Kind == ScanTargetKinds.Image).ToDictionary(i => i.Image, StringComparer.Ordinal),
                now,
                null,
                report.DatabaseUpdatedAt,
                report.Images.Where(i => i.Kind == ScanTargetKinds.Release).DistinctBy(i => i.Image, StringComparer.OrdinalIgnoreCase).ToDictionary(i => i.Image, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var reason = ScannerReason(exception);
            LogScannerFailed(reason);
            return kept with { At = now, Error = reason };
        }
    }

    // A request that fails does not stop the read: the read is the source of truth (the scanner may answer it while it refuses new work).
    private async Task RequestScansAsync(IImageScanner scanner, List<ScanTarget> targets, CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            await scanner.RequestAsync(targets, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogScannerRequestFailed(ScannerReason(exception));
        }
    }

    /// <summary>
    /// The exploit intelligence (D-133), when it is on and an image scanner is declared (it enriches the scanner's findings, nothing else): the catalog of
    /// known exploited vulnerabilities whole, and the predictions of the CVE ids the scanner reports now (every finding's, not only those it describes), once a day (an hour after a failure). A CVE id
    /// that appears in between gets its prediction at the next daily read (its exploitation is known at once: the catalog is whole); a read that had no id to
    /// ask about is made again as soon as there are some. A part that cannot be read keeps the previous data, with the error next to it.
    /// </summary>
    private async Task<ThreatIntelSnapshot> ReadThreatIntelAsync(ThreatIntelSnapshot previous, ImageScanSnapshot scans, CancellationToken cancellationToken)
    {
        var source = _exploitIntelligence.FirstOrDefault();
        if (!_threatIntel || source is null || _catalogService.Current.Catalog.ImageScanner is null)
        {
            return ThreatIntelSnapshot.Off;
        }

        var current = previous.Enabled ? previous : ThreatIntelSnapshot.Waiting;
        var now = _timeProvider.GetUtcNow();
        var ids = ExploitIntelligence.CveIdsOf(scans);
        var interval = current.Error is null ? ThreatIntelInterval : ThreatIntelRetryInterval;
        if (current.At is { } at && now - at < interval && !(_threatIntelAskedNone && ids.Count > 0))
        {
            return current;
        }

        string? error = null;
        var knownExploited = current.KnownExploited;
        try
        {
            knownExploited = await source.ReadKnownExploitedAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            error = ThreatIntelReason(exception);
            LogThreatIntelFailed("known exploited vulnerabilities", error);
        }

        var predictions = current.Predictions;
        if (ids.Count > 0)
        {
            try
            {
                predictions = await source.ReadPredictionsAsync(ids, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var reason = ThreatIntelReason(exception);
                error ??= reason;
                LogThreatIntelFailed("exploit predictions", reason);
            }
        }

        _threatIntelAskedNone = ids.Count == 0;
        return new ThreatIntelSnapshot(true, knownExploited, predictions, now, error);
    }

    // The whole read has a time limit: a stream cut by it says OperationCanceledException, which is a timeout here (the pass itself was not cancelled).
    private static string ThreatIntelReason(Exception exception) => exception switch
    {
        ExploitIntelligenceUnavailableException unavailable => unavailable.Reason,
        OperationCanceledException => ReasonCodes.Timeout,
        _ => ReasonCodes.From(exception),
    };

    private static string ScannerReason(Exception exception) => exception is ImageScannerUnavailableException unavailable ? unavailable.Reason : ReasonCodes.From(exception);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image scanner could not be read: {Reason}")]
    private partial void LogScannerFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image scanner could not be asked to scan: {Reason}")]
    private partial void LogScannerRequestFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The exploit intelligence ({Part}) could not be read: {Reason}; the previous data is kept")]
    private partial void LogThreatIntelFailed(string part, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry lookup of {Repository} failed: {Reason}")]
    private partial void LogLookupFailed(string repository, string reason);
}
