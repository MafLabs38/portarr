using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Backups;

/// <summary>
/// What one backup source said of one environment: its jobs, or why it could not tell (<c>Error</c>: a reason
/// code, <c>no-connector</c> when nothing reads that tool there). <c>NotSetUp</c>: the error is something the operator has to
/// configure (no connector, a missing credential), not a tool that failed.
/// </summary>
public sealed record SourceReading(string Kind, string EnvironmentId, IReadOnlyList<BackupJob> Jobs, string? Error, DateTimeOffset CheckedAt, bool NotSetUp = false)
{
    public static string KeyOf(string kind, string environmentId) => $"{kind}@{environmentId}";

    public string Key => KeyOf(Kind, EnvironmentId);
}

public sealed record BackupMonitorState(IReadOnlyDictionary<string, SourceReading> Readings)
{
    public static BackupMonitorState Empty { get; } = new(new Dictionary<string, SourceReading>());
}

public interface IBackupMonitor
{
    /// <summary>What the sources the catalog names said at their last read. In memory only.</summary>
    BackupMonitorState Current { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks, every minute, the backup tools the catalog's data names how their jobs ran (D-62). Backups run daily or
/// weekly: a minute is plenty, and keeps the load on the tools negligible. Only the tools a followed data asset
/// expects are read. Nothing is stored: a restart asks again.
/// </summary>
public sealed partial class BackupMonitor : BackgroundService, IBackupMonitor
{
    public const string NoConnector = "no-connector";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ICatalogService _catalogService;
    private readonly IEnumerable<IBackupSource> _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupMonitor> _logger;
    private volatile BackupMonitorState _current = BackupMonitorState.Empty;

    public BackupMonitor(ICatalogService catalogService, IEnumerable<IBackupSource> sources, TimeProvider timeProvider, ILogger<BackupMonitor> logger)
    {
        _catalogService = catalogService;
        _sources = sources;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public BackupMonitorState Current => _current;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var catalog = _catalogService.Current.Catalog;
        var environments = catalog.Environments.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var wanted = catalog.DataAssets
            .SelectMany(asset => asset.Targets.SelectMany(target => target.Sources))
            .Select(source => (source.Kind, source.EnvironmentId))
            .Distinct()
            .ToList();

        var readings = await Task.WhenAll(wanted.Select(w => environments.TryGetValue(w.EnvironmentId, out var environment)
            ? ReadAsync(w.Kind, environment, cancellationToken)
            : Task.FromResult(new SourceReading(w.Kind, w.EnvironmentId, [], NoConnector, _timeProvider.GetUtcNow(), NotSetUp: true))));
        _current = new BackupMonitorState(readings.ToDictionary(r => r.Key, StringComparer.Ordinal));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _timeProvider);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogRefreshFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<SourceReading> ReadAsync(string kind, CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var source = _sources.FirstOrDefault(s => s.Kind == kind && s.Supports(environment));
        if (source is null)
        {
            return new SourceReading(kind, environment.Id, [], NoConnector, now, NotSetUp: true);
        }

        try
        {
            return new SourceReading(kind, environment.Id, await source.ListJobsAsync(environment, cancellationToken), null, now);
        }
        catch (BackupSourceUnavailableException unavailable)
        {
            return new SourceReading(kind, environment.Id, [], unavailable.Reason, now, unavailable.NotSetUp);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSourceFailed($"{kind}@{environment.Id}", exception.Message);
            return new SourceReading(kind, environment.Id, [], ReasonCodes.From(exception), now);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup refresh failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup source {Source} could not be read: {Reason}")]
    private partial void LogSourceFailed(string source, string reason);
}
