using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Infrastructure;

/// <summary>
/// A point of a machine's live chart. <c>GuestCpuCores</c> is, on a hypervisor, the load its VMs report from
/// the inside at that moment (D-59): guest-reported, not measured by the host.
/// </summary>
public sealed record MachinePoint(DateTimeOffset At, double? CpuCores, long? MemoryBytes, double? GuestCpuCores = null);

/// <summary>
/// A VM of a hypervisor, as its own environment sees it from the inside (D-59): its size (cores and memory
/// allocated to it, as the guest reports them) and its load. Usage is null until the guest is sampled, or
/// while its environment cannot be read.
/// </summary>
public sealed record GuestMachine(
    string EnvironmentId,
    string MachineId,
    string Name,
    MachineKind? Kind,
    double? CpuCapacityCores,
    long? MemoryCapacityBytes,
    double? CpuCores,
    UsageScope? Scope);

/// <summary>
/// A machine's latest sample and its recent points, oldest first. <c>HostEnvironmentId</c> is the hypervisor
/// it is a VM on; <c>Guests</c> are the VMs it hosts (both from the catalog, D-59).
/// </summary>
public sealed record MachineView(MachineSample Machine, IReadOnlyList<MachinePoint> History, string? HostEnvironmentId = null, IReadOnlyList<GuestMachine>? Guests = null);

/// <summary>
/// The machines of an environment and the usage of its workloads by workload id. <c>Error</c> is a reason
/// code (the environment is not connected, unreachable, or its sampling failed); the history is kept.
/// <c>SampledAt</c> is the time of the newest measurement; <c>Interval</c> how often it is read, and
/// <c>Resolution</c> how often its values really change.
/// </summary>
public sealed record EnvironmentMetrics(
    string EnvironmentId,
    IReadOnlyList<MachineView> Machines,
    IReadOnlyDictionary<string, WorkloadUsageSample> Workloads,
    DateTimeOffset? SampledAt,
    string? Error,
    TimeSpan Interval = default,
    TimeSpan? Resolution = null);

public interface IInfrastructureMonitor
{
    /// <summary>Latest metrics per environment id. In memory only, a live window (D-54).</summary>
    IReadOnlyDictionary<string, EnvironmentMetrics> Current { get; }

    /// <summary>Samples every environment once, now (each one is otherwise read at its connector's interval).</summary>
    Task SampleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Samples every environment whose connector sees machines, each at its connector's <see cref="IInfrastructureSource.Interval"/>
/// (a few seconds), and keeps the last <see cref="Window"/> of points per machine in a ring buffer (D-54).
/// Never persisted: a restart starts an empty chart, and long history is the job of a metrics tool
/// (Prometheus, V8). Environments are sampled independently, each with its own timeout, so a slow one cannot
/// delay the others. A hypervisor's machine also carries its VMs, as they report themselves (D-59).
/// </summary>
public sealed partial class InfrastructureMonitor : BackgroundService, IInfrastructureMonitor
{
    /// <summary>The live window kept per machine.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Hard bound of the ring buffer, whatever the interval: one point per second over the window.</summary>
    public static readonly int Capacity = (int)Window.TotalSeconds;

    /// <summary>How often the scheduler looks for environments due for a sample.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan SampleTimeout = TimeSpan.FromSeconds(10);

    private readonly ICatalogService _catalogService;
    private readonly IWorkloadInventory _inventory;
    private readonly IEnumerable<IInfrastructureSource> _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InfrastructureMonitor> _logger;

    // Written under _gate only (the network reads run outside it); readers get immutable snapshots.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, Queue<MachinePoint>>> _history = new(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, EnvironmentMetrics> _current = new Dictionary<string, EnvironmentMetrics>(StringComparer.Ordinal);

    public InfrastructureMonitor(
        ICatalogService catalogService,
        IWorkloadInventory inventory,
        IEnumerable<IInfrastructureSource> sources,
        TimeProvider timeProvider,
        ILogger<InfrastructureMonitor> logger)
    {
        _catalogService = catalogService;
        _inventory = inventory;
        _sources = sources;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, EnvironmentMetrics> Current => _current;

    public async Task SampleAsync(CancellationToken cancellationToken = default)
    {
        var catalog = _catalogService.Current.Catalog;
        await Task.WhenAll(catalog.Environments.Select(environment => SampleEnvironmentAsync(environment, cancellationToken)));
        Prune(catalog);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new Dictionary<string, Task>(StringComparer.Ordinal);
        var due = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using var timer = new PeriodicTimer(Tick, _timeProvider);
        do
        {
            try
            {
                StartDueSamples(running, due, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed tick must never end the service (and with it the host).
                LogSamplingFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Starts, without waiting for it, the sample of every environment due and not already being read.</summary>
    private void StartDueSamples(Dictionary<string, Task> running, Dictionary<string, DateTimeOffset> due, CancellationToken stoppingToken)
    {
        foreach (var done in running.Where(r => r.Value.IsCompleted).Select(r => r.Key).ToList())
        {
            running.Remove(done);
        }

        var catalog = _catalogService.Current.Catalog;
        var now = _timeProvider.GetUtcNow();
        foreach (var environment in catalog.Environments)
        {
            var source = _sources.FirstOrDefault(s => s.Supports(environment));
            if (source is null || running.ContainsKey(environment.Id) || (due.TryGetValue(environment.Id, out var at) && at > now))
            {
                continue;
            }

            due[environment.Id] = now + source.Interval;
            running[environment.Id] = SampleEnvironmentAsync(environment, stoppingToken);
        }

        foreach (var gone in due.Keys.Where(id => catalog.Environments.All(e => e.Id != id)).ToList())
        {
            due.Remove(gone);
        }

        Prune(catalog);
    }

    /// <summary>Reads one environment and stores its metrics; never throws (a failure is stored as a reason code).</summary>
    private async Task SampleEnvironmentAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Supports(environment));
        var inventory = _inventory.Current.GetValueOrDefault(environment.Id);
        if (source is null || inventory is null)
        {
            // No machine to show (Cloudflare, launcher-only), or not inventoried yet.
            return;
        }

        if (inventory.NotConfigured || !inventory.Reachable)
        {
            Store(environment, source, null, inventory.Error ?? ReasonCodes.Unreachable);
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SampleTimeout);
            var sample = await source.SampleAsync(environment, inventory.Workloads, timeout.Token);
            Store(environment, source, sample, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSampleFailed(environment.Id, exception.Message);
            Store(environment, source, null, ReasonCodes.From(exception));
        }
    }

    /// <summary>
    /// Appends the sample to the history of its machines, or keeps the last machines and their history
    /// (flagged by the error) so a chart does not vanish on a blip.
    /// </summary>
    private void Store(CatalogEnvironment environment, IInfrastructureSource source, InfrastructureSample? sample, string? error)
    {
        lock (_gate)
        {
            var catalog = _catalogService.Current.Catalog;
            var previous = _current.GetValueOrDefault(environment.Id);
            EnvironmentMetrics metrics;
            if (sample is null)
            {
                metrics = new EnvironmentMetrics(
                    environment.Id,
                    previous?.Machines ?? [],
                    previous?.Workloads ?? new Dictionary<string, WorkloadUsageSample>(),
                    previous?.SampledAt,
                    error,
                    source.Interval,
                    previous?.Resolution);
            }
            else
            {
                var now = _timeProvider.GetUtcNow();
                var machines = Append(environment, sample.Machines, catalog, now);
                var workloads = sample.Workloads
                    .GroupBy(w => w.WorkloadId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                var sampledAt = machines.Select(m => m.History.LastOrDefault()?.At).Max() ?? now;
                metrics = new EnvironmentMetrics(environment.Id, machines, workloads, sampledAt, null, source.Interval, sample.Resolution);
            }

            _current = new Dictionary<string, EnvironmentMetrics>(_current, StringComparer.Ordinal) { [environment.Id] = metrics };
        }
    }

    private List<MachineView> Append(CatalogEnvironment environment, IReadOnlyList<MachineSample> machines, Catalog.Catalog catalog, DateTimeOffset now)
    {
        if (!_history.TryGetValue(environment.Id, out var history))
        {
            history = new Dictionary<string, Queue<MachinePoint>>(StringComparer.Ordinal);
            _history[environment.Id] = history;
        }

        foreach (var gone in history.Keys.Where(id => machines.All(m => m.Id != id)).ToList())
        {
            history.Remove(gone);
        }

        var guests = GuestsOf(environment.Id, catalog);
        var guestCpu = guests.Where(g => g.CpuCores is not null).Select(g => g.CpuCores!.Value).ToList();
        double? guestCpuTotal = guestCpu.Count > 0 ? Math.Round(guestCpu.Sum(), 3) : null;
        return machines.Select(machine => Record(history, machine, environment, now, guests, guestCpuTotal)).ToList();
    }

    /// <summary>One machine: its new point when one is due, the history pruned to the live window, and its view.</summary>
    private static MachineView Record(Dictionary<string, Queue<MachinePoint>> history, MachineSample machine, CatalogEnvironment environment, DateTimeOffset now, List<GuestMachine> guests, double? guestCpuTotal)
    {
        if (!history.TryGetValue(machine.Id, out var points))
        {
            points = new Queue<MachinePoint>();
            history[machine.Id] = points;
        }

        var at = machine.MeasuredAt ?? now;

        // metrics.k8s.io refreshes every ~15 s: polling faster must not invent points (nor interpolate).
        if (points.Count == 0 || points.Last().At < at)
        {
            points.Enqueue(new MachinePoint(at, machine.CpuCores, machine.MemoryBytes, guestCpuTotal));
        }

        while (points.Count > Capacity || (points.Count > 0 && points.Peek().At < now - Window))
        {
            points.Dequeue();
        }

        return new MachineView(machine, points.ToArray(), environment.HostOf(machine.Id), guests.Count > 0 ? guests : null);
    }

    /// <summary>
    /// The VMs declared on a hypervisor environment (D-59), with their latest inside view: a Docker guest is
    /// its environment's machine, a Kubernetes guest one of its nodes (<c>nodeHosts</c>).
    /// </summary>
    private List<GuestMachine> GuestsOf(string hostEnvironmentId, Catalog.Catalog catalog)
    {
        var guests = new List<GuestMachine>();
        foreach (var environment in catalog.Environments.Where(e => e.Id != hostEnvironmentId))
        {
            var metrics = _current.GetValueOrDefault(environment.Id);
            var sampled = metrics?.Machines ?? [];
            var declared = environment.Kubernetes is null ? [environment.Id] : environment.Kubernetes.NodeHosts?.Keys ?? [];
            var ids = sampled.Select(m => m.Machine.Id).Union(declared, StringComparer.Ordinal);

            foreach (var id in ids.Where(id => environment.HostOf(id) == hostEnvironmentId))
            {
                var machine = sampled.FirstOrDefault(m => m.Machine.Id == id)?.Machine;
                guests.Add(new GuestMachine(
                    environment.Id,
                    id,
                    machine?.Name ?? (environment.Kubernetes is null ? environment.Name : id),
                    machine?.Kind,
                    machine?.CpuCapacityCores,
                    machine?.MemoryCapacityBytes,
                    metrics?.Error is null ? machine?.CpuCores : null,
                    machine?.Scope));
            }
        }

        return guests;
    }

    /// <summary>Forgets the environments that left the catalog.</summary>
    private void Prune(Catalog.Catalog catalog)
    {
        lock (_gate)
        {
            var ids = catalog.Environments.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            if (_current.Keys.All(ids.Contains))
            {
                return;
            }

            foreach (var gone in _history.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _history.Remove(gone);
            }

            _current = _current.Where(e => ids.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Infrastructure sampling failed")]
    private partial void LogSamplingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sampling of environment {EnvironmentId} failed: {Reason}")]
    private partial void LogSampleFailed(string environmentId, string reason);
}
