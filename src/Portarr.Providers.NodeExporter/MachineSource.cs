using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.NodeExporter;

/// <summary>
/// The workloads and the machine of a <c>runtime: machine</c> environment (D-126): no workload, and one machine whose figures are
/// left to its host agent (node_exporter) and its card (<c>nvidiaGpu</c>). Nothing is read here: there is no engine to ask.
/// </summary>
public sealed class MachineSource : IWorkloadSource, IInfrastructureSource
{
    /// <summary>What a machine without an engine is called on the dashboard.</summary>
    public const string MachineKind = "host";

    public bool SupportsUsage => false;

    public bool SupportsLogs => false;

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.IsMachineOnly();
    }

    public Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Workload>>([]);
    }

    public Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        return Task.FromResult<WorkloadUsage?>(null);
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        return Task.FromResult<ILogStream?>(null);
    }

    public Task<InfrastructureSample> SampleAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken)
    {
        var machine = new MachineSample(environment.Id, environment.Name, MachineKind, null, null, null, null, UsageScope.Host);
        return Task.FromResult(new InfrastructureSample([machine], []));
    }
}
