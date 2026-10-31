using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Infrastructure;

public interface IHostMetricsMonitor
{
    /// <summary>Latest host agent view per machine. In memory only, a live window (D-72).</summary>
    IReadOnlyDictionary<HostMachineKey, HostMetricsView> Current { get; }

    /// <summary>
    /// Every new read of a machine, as it happens (one per machine per second), until <paramref name="cancellationToken"/>
    /// is cancelled. A slow reader loses the oldest reads, never blocks the monitor. Views carry no history.
    /// </summary>
    IAsyncEnumerable<HostMetricsView> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the host agent of every machine that declares one (D-72), <b>every second</b>, like Unraid's dashboard
/// publisher reads /proc/stat, and keeps the last <see cref="InfrastructureMonitor.Window"/> of points per machine.
/// Never persisted (D-54). A Docker host's agent comes from the catalog (<c>hostMetrics</c>); a cluster's nodes
/// come from the Infrastructure monitor's last sample (name and InternalIP) and <c>kubernetes.nodeExporter</c>.
/// Each machine is read on its own, with a timeout, and never twice at once; a machine whose agent fails is
/// retried after 1, 2, 4… up to 30 s, with the reason kept.
/// </summary>
public sealed partial class HostMetricsMonitor : BackgroundService, IHostMetricsMonitor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private const int SubscriberBuffer = 32;

    private readonly ICatalogService _catalogService;
    private readonly IInfrastructureMonitor _infrastructure;
    private readonly IEnumerable<IHostMetricsSource> _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HostMetricsMonitor> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<HostMachineKey, Queue<HostPoint>> _history = [];
    private readonly Dictionary<HostMachineKey, (int Failures, DateTimeOffset Due)> _schedule = [];
    private readonly Dictionary<HostMachineKey, Task> _running = [];
    private readonly List<Channel<HostMetricsView>> _subscribers = [];
    private volatile IReadOnlyDictionary<HostMachineKey, HostMetricsView> _current = new Dictionary<HostMachineKey, HostMetricsView>();

    public HostMetricsMonitor(
        ICatalogService catalogService,
        IInfrastructureMonitor infrastructure,
        IEnumerable<IHostMetricsSource> sources,
        TimeProvider timeProvider,
        ILogger<HostMetricsMonitor> logger)
    {
        _catalogService = catalogService;
        _infrastructure = infrastructure;
        _sources = sources;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyDictionary<HostMachineKey, HostMetricsView> Current => _current;

    public async IAsyncEnumerable<HostMetricsView> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<HostMetricsView>(new BoundedChannelOptions(SubscriberBuffer) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var view in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return view;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    /// <summary>Reads every machine once, now, and waits for the reads (tests, and the first tick).</summary>
    public async Task SampleAsync(CancellationToken cancellationToken = default)
    {
        StartDueReads(cancellationToken);
        Task[] running;
        lock (_gate)
        {
            running = [.. _running.Values];
        }

        await Task.WhenAll(running);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _timeProvider);
        do
        {
            try
            {
                StartDueReads(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed tick must never end the service (and with it the host).
                LogTickFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>The machines whose host agent is declared, with where to read it.</summary>
    public static IReadOnlyList<HostMetricsTarget> ResolveTargets(Catalog.Catalog catalog, IReadOnlyDictionary<string, EnvironmentMetrics> infrastructure)
    {
        var targets = new List<HostMetricsTarget>();
        foreach (var environment in catalog.Environments)
        {
            if (environment.HostMetrics is { } endpoint)
            {
                targets.Add(new HostMetricsTarget(environment, environment.Id, endpoint.NodeExporter));
                continue;
            }

            if (environment.Kubernetes?.NodeExporter is not { } nodeExporter)
            {
                continue;
            }

            foreach (var node in (infrastructure.GetValueOrDefault(environment.Id)?.Machines ?? []).Select(machine => machine.Machine))
            {
                var route = nodeExporter.RouteOf(node.Id);
                HostMetricsTarget? target = route.Access switch
                {
                    NodeExporterAccess.Url => new HostMetricsTarget(environment, node.Id, route.Url),
                    NodeExporterAccess.ApiServer => new HostMetricsTarget(environment, node.Id, null, nodeExporter.Port),
                    NodeExporterAccess.Direct when node.Address is { } address
                        => IsReachableAddress(address)
                            ? new HostMetricsTarget(environment, node.Id, new UriBuilder(Uri.UriSchemeHttp, address, nodeExporter.Port, "/metrics").Uri)
                            : new HostMetricsTarget(environment, node.Id, null, null, InvalidAddress),
                    _ => null,
                };
                if (target is not null)
                {
                    targets.Add(target);
                }
            }
        }

        return targets;
    }

    /// <summary>
    /// A node address worth connecting to: a node that reports a loopback, link-local or unspecified address (or a name that is not an address
    /// or a host name) would make Portarr read itself or its own network neighbours, so it is skipped.
    /// </summary>
    internal static bool IsReachableAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (IPAddress.TryParse(address, out var ip))
        {
            ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
            return !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) && !ip.IsIPv6LinkLocal && !IsIPv4LinkLocal(ip);
        }

        return Uri.CheckHostName(address) == UriHostNameType.Dns && !address.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIPv4LinkLocal(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>The reason code of a node whose reported address is not one Portarr reads (D-32): use <c>nodeExporter.nodes</c> for it.</summary>
    internal const string InvalidAddress = "invalid-address";

    private void StartDueReads(CancellationToken stoppingToken)
    {
        var targets = ResolveTargets(_catalogService.Current.Catalog, _infrastructure.Current);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            foreach (var done in _running.Where(r => r.Value.IsCompleted).Select(r => r.Key).ToList())
            {
                _running.Remove(done);
            }

            foreach (var target in targets)
            {
                var source = _sources.FirstOrDefault(s => s.Supports(target));
                if ((source is null && target.ConfigurationError is null) || _running.ContainsKey(target.Key) || (_schedule.TryGetValue(target.Key, out var schedule) && schedule.Due > now))
                {
                    continue;
                }

                _running[target.Key] = ReadAsync(source, target, stoppingToken);
            }

            Prune(targets.Select(t => t.Key).ToHashSet());
        }
    }

    /// <summary>Reads one machine and stores the result; never throws (a failure is stored as a reason code).</summary>
    private async Task ReadAsync(IHostMetricsSource? source, HostMetricsTarget target, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            if ((target.ConfigurationError ?? source?.GetConfigurationError(target)) is { Length: > 0 } configurationError)
            {
                Store(target.Key, null, configurationError);
                return;
            }

            if (source is null)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadTimeout);
            var reading = await source.ReadAsync(target, timeout.Token);
            Store(target.Key, reading, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogReadFailed(target.Environment.Id, target.MachineId, exception.Message);
            Store(target.Key, null, ReasonCodes.From(exception));
        }
    }

    private void Store(HostMachineKey key, HostReading? reading, string? error)
    {
        HostMetricsView view;
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            var previous = _current.GetValueOrDefault(key);
            if (!_history.TryGetValue(key, out var points))
            {
                points = new Queue<HostPoint>();
                _history[key] = points;
            }

            if (reading is null)
            {
                var failures = _schedule.GetValueOrDefault(key).Failures + 1;
                var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failures - 1), MaxBackoff.TotalSeconds));
                _schedule[key] = (failures, now + backoff - TimeSpan.FromMilliseconds(100));
                view = new HostMetricsView(key, previous?.Latest, previous?.SampledAt, points.ToArray(), error);
            }
            else
            {
                _schedule.Remove(key);
                points.Enqueue(new HostPoint(now, reading.CpuPercent, reading.IowaitPercent, reading.Memory.UsedBytes, reading.Memory.SwapUsedBytes));
                while (points.Count > InfrastructureMonitor.Capacity || (points.Count > 0 && points.Peek().At < now - InfrastructureMonitor.Window))
                {
                    points.Dequeue();
                }

                view = new HostMetricsView(key, reading, now, points.ToArray(), null);
            }

            _current = new Dictionary<HostMachineKey, HostMetricsView>(_current) { [key] = view };
            var live = view with { History = [] };
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(live);
            }
        }
    }

    /// <summary>Forgets the machines that no longer declare a host agent (or left the catalog). Called under the gate.</summary>
    private void Prune(HashSet<HostMachineKey> keys)
    {
        if (_current.Keys.All(keys.Contains) && _history.Keys.All(keys.Contains))
        {
            return;
        }

        foreach (var gone in _history.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _history.Remove(gone);
            _schedule.Remove(gone);
        }

        _current = _current.Where(e => keys.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Host metrics tick failed")]
    private partial void LogTickFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Host agent of {EnvironmentId}/{MachineId} could not be read: {Reason}")]
    private partial void LogReadFailed(string environmentId, string machineId, string reason);
}
