using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;

namespace Portarr.Core.Workloads;

public sealed record EnvironmentInventory(
    string EnvironmentId,
    bool Reachable,
    string? Error,
    IReadOnlyList<Workload> Workloads,
    DateTimeOffset? RefreshedAt);

public interface IWorkloadInventory
{
    /// <summary>Last known workloads per environment id. Kept in memory only (D-03).</summary>
    IReadOnlyDictionary<string, EnvironmentInventory> Current { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Polls every declared environment and keeps the latest inventory.</summary>
public sealed partial class WorkloadInventory : BackgroundService, IWorkloadInventory
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly ICatalogService _catalogService;
    private readonly IEnumerable<IWorkloadSource> _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkloadInventory> _logger;
    private volatile IReadOnlyDictionary<string, EnvironmentInventory> _current = new Dictionary<string, EnvironmentInventory>();

    public WorkloadInventory(
        ICatalogService catalogService,
        IEnumerable<IWorkloadSource> sources,
        TimeProvider timeProvider,
        ILogger<WorkloadInventory> logger)
    {
        _catalogService = catalogService;
        _sources = sources;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // Read by request threads while a poll replaces it: always a whole, published dictionary.
    public IReadOnlyDictionary<string, EnvironmentInventory> Current
    {
        get => _current;
        private set => _current = value;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var environments = _catalogService.Current.Catalog.Environments;
        var results = await Task.WhenAll(environments.Select(environment => RefreshEnvironmentAsync(environment, cancellationToken)));

        Current = results.Where(r => r is not null).ToDictionary(r => r!.EnvironmentId, r => r!);
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
                // A failed tick must never end the service (and with it the host).
                LogRefreshFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<EnvironmentInventory?> RefreshEnvironmentAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Supports(environment));
        if (source is null)
        {
            return null;
        }

        try
        {
            var workloads = await source.ListAsync(environment, cancellationToken);
            return new EnvironmentInventory(environment.Id, true, null, workloads, _timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Details stay in the logs; the API only exposes a reason code (no internal hosts or ports).
            LogUnreachable(environment.Id, exception.Message);

            // Keep the last known workloads so the UI can show what was there, flagged as stale.
            var previous = Current.GetValueOrDefault(environment.Id);
            return new EnvironmentInventory(environment.Id, false, ReasonCode(exception), previous?.Workloads ?? [], previous?.RefreshedAt);
        }
    }

    private static string ReasonCode(Exception exception)
    {
        return exception switch
        {
            TaskCanceledException or TimeoutException => "timeout",
            HttpRequestException { StatusCode: { } status } => $"http-{(int)status}",
            HttpRequestException or IOException => "unreachable",
            _ => "invalid-response",
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Inventory refresh failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Environment {EnvironmentId} unreachable: {Reason}")]
    private partial void LogUnreachable(string environmentId, string reason);
}
