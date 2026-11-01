using Portarr.Core.Catalog;

namespace Portarr.Core.Infrastructure;

/// <summary>
/// Capability of a connector that reads a host agent on a machine (node_exporter, D-72): what the machine
/// itself does, as its kernel counts it, including what its own connector cannot see (a hypervisor's VMs, the
/// host's processes, I/O wait). Read-only, read every second by <see cref="HostMetricsMonitor"/>.
/// </summary>
public interface IHostMetricsSource
{
    bool Supports(HostMetricsTarget target);

    /// <summary>
    /// A reason code when the machine's agent cannot be read yet (a token is declared but its variable is empty), checked before
    /// every read: it is shown as that reason, like a workload source's configuration error (D-32), without calling the agent.
    /// </summary>
    string? GetConfigurationError(HostMetricsTarget target) => null;

    /// <summary>
    /// The machines of <paramref name="environment"/> whose agent this connector reads (D-105): where each is read, from what the
    /// environment declares and, for a cluster, from what the infrastructure monitor last saw of its nodes. None by default.
    /// </summary>
    IEnumerable<HostMetricsTarget> TargetsOf(CatalogEnvironment environment, EnvironmentMetrics? infrastructure) => [];

    /// <summary>
    /// Reads the machine once. The CPU is measured between two reads: the first read of a target (or the
    /// first after its counters went backwards, e.g. a reboot) has no CPU yet. Throws when the agent cannot be read.
    /// </summary>
    Task<HostReading> ReadAsync(HostMetricsTarget target, CancellationToken cancellationToken);
}

/// <summary>A machine of the dashboard, by its environment and its machine id (the environment id for a Docker host, the node name).</summary>
public readonly record struct HostMachineKey(string EnvironmentId, string MachineId);

/// <summary>
/// Where a machine's host agent is read: <c>Url</c> directly, or, when null, by the <c>Route</c> its connector gave (a Kubernetes
/// node's exporter is reached through the API server's pod proxy: Core does not know what a route holds). <c>ConfigurationError</c>
/// is a reason code when the machine cannot be read as declared.
/// </summary>
public sealed record HostMetricsTarget(CatalogEnvironment Environment, string MachineId, Uri? Url, object? Route = null, string? ConfigurationError = null)
{
    public HostMachineKey Key => new(Environment.Id, MachineId);
}

/// <summary>
/// One read of a host agent. Percentages are 0–100 of the whole machine, measured since the previous read
/// (null on the first one): <c>CpuPercent</c> is 100 − idle (I/O wait included, like top and Unraid),
/// <c>IowaitPercent</c> the part spent waiting for I/O, <c>CorePercents</c> the same load per logical core.
/// </summary>
public sealed record HostReading(
    int Cores,
    double? CpuPercent,
    double? IowaitPercent,
    IReadOnlyList<double>? CorePercents,
    HostMemory Memory,
    double? Load1,
    double? TemperatureCelsius);

/// <summary>
/// The kernel's memory counters (/proc/meminfo, bytes). <c>Used</c> is Total − Available: what cannot be given
/// back without swapping. <c>Reclaimable</c> is Available − Free: the cache the kernel gives back on demand.
/// Used + Reclaimable + Free = Total. <c>KernelCacheBytes</c> (Buffers + Cached + SReclaimable) also counts
/// tmpfs and shared memory, which are not reclaimable: it is informative only.
/// </summary>
public sealed record HostMemory(long TotalBytes, long AvailableBytes, long FreeBytes, long KernelCacheBytes, long SwapTotalBytes, long SwapFreeBytes)
{
    public long UsedBytes => Math.Max(TotalBytes - AvailableBytes, 0);

    public long ReclaimableBytes => Math.Max(AvailableBytes - FreeBytes, 0);

    public long SwapUsedBytes => Math.Max(SwapTotalBytes - SwapFreeBytes, 0);
}

/// <summary>A point of a machine's host chart, one per second (D-72).</summary>
public sealed record HostPoint(DateTimeOffset At, double? CpuPercent, double? IowaitPercent, long MemoryUsedBytes, long SwapUsedBytes);

/// <summary>
/// A machine's host agent: its latest read, when it was taken, its points over the live window (oldest
/// first) and, when the last read failed, why (a reason code; the last read and the points are kept).
/// </summary>
public sealed record HostMetricsView(HostMachineKey Key, HostReading? Latest, DateTimeOffset? SampledAt, IReadOnlyList<HostPoint> History, string? Error);
