using Portarr.Core.Catalog;

namespace Portarr.Core.Workloads;

/// <summary>
/// Capability of a connector that discovers workloads in an environment (Docker, Cloudflare, Kubernetes;
/// hypervisors later). Read-only by design (D-31).
/// </summary>
public interface IWorkloadSource
{
    /// <summary>Whether this source can read the environment (e.g. it declares a Docker endpoint).</summary>
    bool Supports(CatalogEnvironment environment);

    /// <summary>
    /// A reason code when the environment is declared but cannot be connected yet (e.g. <c>token-missing</c>):
    /// it is shown as not connected (D-32), without calling the connector.
    /// </summary>
    string? GetConfigurationError(CatalogEnvironment environment) => null;

    /// <summary>Whether <see cref="GetUsageAsync(CatalogEnvironment, Workload, CancellationToken)"/> can return data.</summary>
    bool SupportsUsage => true;

    /// <summary>Whether <see cref="OpenLogsAsync"/> can return data.</summary>
    bool SupportsLogs => true;

    Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken);

    Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken);

    /// <summary>
    /// Usage of a listed workload. Connectors whose names are not unique in an environment (Kubernetes pods
    /// are unique per namespace) override it to use the workload's identity.
    /// </summary>
    Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, Workload workload, CancellationToken cancellationToken)
    {
        return GetUsageAsync(environment, workload.Name, cancellationToken);
    }

    /// <summary>
    /// Opens the logs: the last <paramref name="tail"/> lines, then new lines as they are written when
    /// <paramref name="follow"/> is set. Returns null when the workload does not exist; throws when the
    /// environment cannot serve them, so callers can report it before streaming anything.
    /// </summary>
    Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken);
}

/// <summary>An open log stream; ends when the workload stops writing (it exited) or is disposed.</summary>
public interface ILogStream : IAsyncDisposable
{
    IAsyncEnumerable<LogLine> ReadAsync(CancellationToken cancellationToken);
}
