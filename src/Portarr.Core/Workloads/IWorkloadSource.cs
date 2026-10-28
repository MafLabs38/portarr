using Portarr.Core.Catalog;

namespace Portarr.Core.Workloads;

/// <summary>
/// Capability of a connector that discovers workloads in an environment (Docker for now;
/// Kubernetes and hypervisors later). Read-only by design (D-31).
/// </summary>
public interface IWorkloadSource
{
    /// <summary>Whether this source can read the environment (e.g. it declares a Docker endpoint).</summary>
    bool Supports(CatalogEnvironment environment);

    Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken);

    Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken);

    /// <summary>The last <paramref name="tail"/> lines, then new lines as they are written when <paramref name="follow"/> is set.</summary>
    IAsyncEnumerable<LogLine> StreamLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken);
}
