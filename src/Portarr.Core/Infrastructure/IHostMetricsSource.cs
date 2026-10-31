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
    /// Reads the machine once. The CPU is measured between two reads: the first read of a target (or the
    /// first after its counters went backwards, e.g. a reboot) has no CPU yet. Throws when the agent cannot be read.
    /// </summary>
    Task<HostReading> ReadAsync(HostMetricsTarget target, CancellationToken cancellationToken);
}

/// <summary>A machine of the dashboard, by its environment and its machine id (the environment id for a Docker host, the node name).</summary>
public readonly record struct HostMachineKey(string EnvironmentId, string MachineId);

/// <summary>
/// Where a machine's host agent is read: <c>Url</c> directly, or, when null, through the Kubernetes API
/// server's pod proxy to the node_exporter pod of the node, on <c>ApiServerPort</c>
/// (<c>/api/v1/namespaces/&lt;ns&gt;/pods/&lt;pod&gt;:&lt;port&gt;/proxy/metrics</c>).
/// </summary>
public sealed record HostMetricsTarget(CatalogEnvironment Environment, string MachineId, Uri? Url, int? ApiServerPort = null)
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
