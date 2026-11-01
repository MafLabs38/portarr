using Portarr.Core.Infrastructure;

namespace Portarr.Api.V1.Infrastructure;

/// <summary>A point of a machine's live chart (oldest first); <c>GuestCpuCores</c>: the load its VMs report (D-59).</summary>
public sealed record MachinePointResource(DateTimeOffset At, double? CpuCores, long? MemoryBytes, double? GuestCpuCores);

/// <summary>The hypervisor a machine is a VM on (D-59): an environment of the catalog.</summary>
public sealed record MachineHostResource(string EnvironmentId, string Name);

/// <summary>
/// A VM on a hypervisor, seen from the inside (D-59): the cores and memory allocated to it as the guest
/// reports them, and its load (<c>Scope</c> containers: only the sum of its containers, a lower bound).
/// </summary>
public sealed record GuestResource(
    string EnvironmentId,
    string MachineId,
    string Name,
    double? CpuCapacityCores,
    long? MemoryCapacityBytes,
    double? CpuCores,
    string? Scope);

/// <summary>A point of a machine's host chart, one per second (D-72): load and I/O wait in % of the machine, memory and swap used.</summary>
public sealed record HostPointResource(DateTimeOffset At, double? CpuPercent, double? IowaitPercent, long MemoryUsedBytes, long SwapUsedBytes);

/// <summary>
/// What a machine's host agent (node_exporter) reads, every second (D-72). <c>CpuPercent</c> = 100 − idle (I/O
/// wait included), <c>IowaitPercent</c> its I/O wait part, <c>CorePercents</c> the load of each logical core,
/// all since the previous read. Memory: used = total − available, reclaimable = available − free (cache the
/// kernel gives back), so used + reclaimable + free = total. <c>Error</c> is the reason code of the last read
/// when it failed (the last values are kept); <c>History</c> is empty on the live stream.
/// </summary>
public sealed record HostMetricsResource(
    string EnvironmentId,
    string MachineId,
    string Source,
    DateTimeOffset? SampledAt,
    string? Error,
    int IntervalSeconds,
    int? Cores,
    double? CpuPercent,
    double? IowaitPercent,
    IReadOnlyList<double> CorePercents,
    double? Load1,
    double? TemperatureCelsius,
    long? MemoryTotalBytes,
    long? MemoryUsedBytes,
    long? MemoryReclaimableBytes,
    long? MemoryFreeBytes,
    long? SwapTotalBytes,
    long? SwapUsedBytes,
    IReadOnlyList<HostPointResource> History);

/// <summary>Usage of a workload on its machine; <c>CpuCores</c> is null until two samples were taken.</summary>
public sealed record MachineWorkloadResource(string Id, double? CpuCores, long? MemoryBytes);

/// <summary>
/// A machine: a Docker host or a Kubernetes node. <c>Kind</c>: docker-host or kubernetes-node. <c>Scope</c>:
/// what its usage measures, <c>containers</c> (the sum of its containers, Docker alone) or <c>host</c>.
/// <c>Workloads</c> lists every workload placed on it (stopped ones without usage), by workload id. <c>Host</c>
/// is the hypervisor it is a VM on, <c>Guests</c> the VMs it hosts (D-59). <c>HostMetrics</c> is what its host
/// agent reads, when it has one (D-72): the machine as its kernel sees it, which wins over <c>CpuCores</c> and
/// <c>MemoryBytes</c> (the sum of its containers, or metrics-server).
/// </summary>
public sealed record MachineResource(
    string Id,
    string Name,
    string Kind,
    IReadOnlyList<string> Roles,
    bool? Ready,
    double? CpuCapacityCores,
    long? MemoryCapacityBytes,
    double? CpuCores,
    long? MemoryBytes,
    string Scope,
    IReadOnlyList<MachinePointResource> History,
    IReadOnlyList<MachineWorkloadResource> Workloads,
    MachineHostResource? Host,
    IReadOnlyList<GuestResource> Guests,
    HostMetricsResource? HostMetrics = null);

/// <summary>
/// An environment with its machines (none for Cloudflare or a launcher-only environment). <c>Error</c> is a
/// reason code; <c>Unplaced</c> lists the workload ids that run on no known machine (pending pods).
/// <c>SampledAt</c> is the newest measurement, <c>IntervalSeconds</c> how often the environment is read,
/// <c>ResolutionSeconds</c> how often its values really change (metrics-server), <c>WindowSeconds</c> the live window.
/// </summary>
public sealed record InfrastructureEnvironmentResource(
    string Id,
    string Name,
    string Runtime,
    DateTimeOffset? SampledAt,
    string? Error,
    int IntervalSeconds,
    int? ResolutionSeconds,
    int WindowSeconds,
    IReadOnlyList<MachineResource> Machines,
    IReadOnlyList<string> Unplaced);

public static class InfrastructureResourceMapper
{
    public static string ToResource(this UsageScope scope) => scope == UsageScope.Containers ? "containers" : "host";

    /// <summary>A host agent's view; <paramref name="withHistory"/> false for the live stream (one point at a time).</summary>
    public static HostMetricsResource ToResource(this HostMetricsView view, bool withHistory = true)
    {
        var latest = view.Latest;
        var memory = latest?.Memory;
        return new HostMetricsResource(
            view.Key.EnvironmentId,
            view.Key.MachineId,
            "node-exporter",
            view.SampledAt,
            view.Error,
            (int)HostMetricsMonitor.Interval.TotalSeconds,
            latest?.Cores,
            latest?.CpuPercent,
            latest?.IowaitPercent,
            latest?.CorePercents ?? [],
            latest?.Load1,
            latest?.TemperatureCelsius,
            memory?.TotalBytes,
            memory?.UsedBytes,
            memory?.ReclaimableBytes,
            memory?.FreeBytes,
            memory?.SwapTotalBytes,
            memory?.SwapUsedBytes,
            withHistory ? view.History.Select(p => new HostPointResource(p.At, p.CpuPercent, p.IowaitPercent, p.MemoryUsedBytes, p.SwapUsedBytes)).ToList() : []);
    }
}
