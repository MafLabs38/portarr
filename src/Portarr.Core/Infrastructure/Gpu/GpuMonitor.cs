using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Infrastructure.Gpu;

/// <summary>
/// Reads the graphics cards of the environments that declare one, every <see cref="Interval"/> (the exporter runs the driver on
/// every read: a card does not need a reading every second), and keeps the live window of each card's points. It also asks the
/// runtime which workloads have a card attached. A failed read keeps the last values and says why. Never writes anywhere.
/// </summary>
public sealed partial class GpuMonitor : BackgroundService, IGpuMonitor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>The live window of a card's chart: ten minutes, like the machine's (D-54).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IReadOnlyList<IGpuSource> _sources;
    private readonly IReadOnlyList<IGpuAttachmentSource> _attachments;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GpuMonitor> _logger;
    private volatile IReadOnlyDictionary<string, GpuView> _current = new Dictionary<string, GpuView>();

    public GpuMonitor(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IEnumerable<IGpuSource> sources,
        IEnumerable<IGpuAttachmentSource> attachments,
        TimeProvider timeProvider,
        ILogger<GpuMonitor> logger)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _sources = sources.ToList();
        _attachments = attachments.ToList();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, GpuView> Current => _current;

    /// <summary>Reads every environment that declares a card, once, now; the others are forgotten.</summary>
    public async Task SampleAsync(CancellationToken cancellationToken = default)
    {
        var environments = _catalogService.Current.Catalog.Environments
            .Select(environment => (Environment: environment, Source: _sources.FirstOrDefault(s => s.Supports(environment))))
            .Where(pair => pair.Source is not null)
            .ToList();

        // Every card is read on its own: an exporter that has gone away holds back its own reading, not the others'.
        var previous = _current;
        var views = await Task.WhenAll(environments.Select(pair => ReadAsync(pair.Environment, pair.Source!, previous.GetValueOrDefault(pair.Environment.Id), cancellationToken)));
        _current = views.ToDictionary(view => view.EnvironmentId, StringComparer.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _timeProvider);
        do
        {
            try
            {
                await SampleAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed pass must never end the service (and with it the host).
                LogPassFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<GpuView> ReadAsync(CatalogEnvironment environment, IGpuSource source, GpuView? previous, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        IReadOnlyList<GpuReading> gpus;
        try
        {
            gpus = await source.ReadAsync(environment, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotRead(environment.Id, exception.GetType().Name);
            return (previous ?? new GpuView(environment.Id, [], new Dictionary<string, IReadOnlyList<GpuPoint>>(), [], null, null)) with { Error = ReasonCodes.From(exception) };
        }

        var attached = await AttachedAsync(environment, previous, cancellationToken);
        return new GpuView(environment.Id, gpus, HistoryOf(gpus, previous, now), attached, now, null);
    }

    /// <summary>The points of the window, each card's own, with the new reading added and what fell out of the window dropped.</summary>
    private static Dictionary<string, IReadOnlyList<GpuPoint>> HistoryOf(IReadOnlyList<GpuReading> gpus, GpuView? previous, DateTimeOffset now)
    {
        var oldest = now - Window;
        var history = new Dictionary<string, IReadOnlyList<GpuPoint>>(StringComparer.Ordinal);
        foreach (var gpu in gpus)
        {
            var points = (previous?.History.GetValueOrDefault(gpu.Id) ?? []).Where(p => p.At >= oldest).ToList();
            points.Add(new GpuPoint(now, gpu.UtilizationPercent, gpu.MemoryUsedBytes, gpu.EncoderPercent));
            history[gpu.Id] = points;
        }

        return history;
    }

    /// <summary>The workloads with a card attached; a runtime that cannot be asked keeps what was known.</summary>
    private async Task<IReadOnlyList<string>> AttachedAsync(CatalogEnvironment environment, GpuView? previous, CancellationToken cancellationToken)
    {
        var source = _attachments.FirstOrDefault(s => s.Supports(environment));
        if (source is null || !_inventory.Current.TryGetValue(environment.Id, out var inventory))
        {
            return previous?.WorkloadIds ?? [];
        }

        try
        {
            var running = inventory.Workloads.Where(w => w.State == WorkloadState.Running).ToList();
            var attached = await source.AttachedAsync(environment, running, cancellationToken);
            return running.Select(w => w.Id).Where(attached.Contains).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotRead(environment.Id, exception.GetType().Name);
            return previous?.WorkloadIds ?? [];
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The GPU pass failed")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A graphics card of {EnvironmentId} could not be read ({Reason})")]
    private partial void LogNotRead(string environmentId, string reason);
}
