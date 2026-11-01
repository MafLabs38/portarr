using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Infrastructure;

/// <summary>
/// Capability of a connector that sees the machines of an environment and what its workloads consume
/// (D-54): one Docker host, or the nodes of a Kubernetes cluster. Read-only, sampled every few seconds and
/// kept in memory only for a short live window (<see cref="InfrastructureMonitor"/>).
/// </summary>
public interface IInfrastructureSource
{
    bool Supports(CatalogEnvironment environment);

    /// <summary>
    /// How often the environment is read (D-54): as fast as the connector's data actually changes, so the
    /// dashboard is live without polling for nothing.
    /// </summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Reads the machines and the usage of the running <paramref name="workloads"/> (the environment's last
    /// inventory). Throws when the environment cannot be read at all; a single workload that cannot be read
    /// is left out.
    /// </summary>
    Task<InfrastructureSample> SampleAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken);
}

/// <summary>
/// One reading of an environment. <c>Resolution</c> is how often its values really change (metrics-server
/// averages over a window of about 15 s); null when every reading is a new measurement.
/// </summary>
public sealed record InfrastructureSample(IReadOnlyList<MachineSample> Machines, IReadOnlyList<WorkloadUsageSample> Workloads, TimeSpan? Resolution = null);

/// <summary>What a machine's usage measures: the sum of its containers (Docker alone) or the whole host.</summary>
public enum UsageScope
{
    Containers,
    Host,
}

/// <summary>
/// A machine at one instant: its <c>Kind</c> (what the connector calls it: <c>docker-host</c>, <c>kubernetes-node</c>, shown as is), its capacity (cores, memory) and its usage, in the units shared by every
/// environment (D-33). Null values are unknown (not readable, or not measured yet). <c>MeasuredAt</c> is when
/// the connector measured it, when it says so (metrics.k8s.io): a reading with the same timestamp as the last
/// point is not a new point. <c>Address</c> is the machine's own address when its connector knows it (a node's
/// InternalIP): a host agent on the machine is reached there (D-72).
/// </summary>
public sealed record MachineSample(
    string Id,
    string Name,
    string Kind,
    double? CpuCapacityCores,
    long? MemoryCapacityBytes,
    double? CpuCores,
    long? MemoryBytes,
    UsageScope Scope,
    IReadOnlyList<string>? Roles = null,
    bool? Ready = null,
    DateTimeOffset? MeasuredAt = null,
    string? Address = null);

/// <summary>Usage of one workload (by its id); <c>CpuCores</c> is null until two samples give a delta.</summary>
public sealed record WorkloadUsageSample(string WorkloadId, double? CpuCores, long MemoryBytes);
