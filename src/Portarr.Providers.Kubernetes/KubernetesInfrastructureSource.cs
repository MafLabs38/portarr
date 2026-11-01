using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// The nodes of a cluster as machines (D-54): capacity from the node, usage of the node and of every pod from
/// metrics.k8s.io (metrics-server). Without metrics-server, the nodes are still listed, without usage.
/// metrics-server refreshes its values about every 15 s: the cluster is read every 5 s so a new value shows
/// within seconds, and the monitor only adds a point when a node's metric timestamp changes (no invented points).
/// </summary>
public sealed class KubernetesInfrastructureSource : IInfrastructureSource
{
    private readonly KubernetesApiClient _client;

    public KubernetesInfrastructureSource(KubernetesApiClient client)
    {
        _client = client;
    }

    /// <summary>Faster than metrics-server's resolution, so a fresh value is never more than 5 s late.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    public TimeSpan Interval => SampleInterval;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Kubernetes() is not null;
    }

    public async Task<InfrastructureSample> SampleAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken)
    {
        var cluster = environment.Kubernetes() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Kubernetes cluster.");

        var nodesTask = _client.GetAsync(cluster, "api/v1/nodes", KubernetesJsonContext.Default.NodeList, cancellationToken);
        var nodeMetricsTask = _client.GetAsync(cluster, "apis/metrics.k8s.io/v1beta1/nodes", KubernetesJsonContext.Default.NodeMetricsList, cancellationToken);
        var podMetricsTask = _client.GetAsync(cluster, "apis/metrics.k8s.io/v1beta1/pods", KubernetesJsonContext.Default.PodMetricsList, cancellationToken);

        var nodes = await nodesTask ?? throw new InvalidDataException("The nodes endpoint was not found.");
        var nodeMetricItems = (await nodeMetricsTask)?.Items ?? [];
        var nodeMetrics = nodeMetricItems.ToDictionary(m => m.Metadata.Name, StringComparer.Ordinal);
        var podMetrics = (await podMetricsTask)?.Items ?? [];

        var machines = nodes.Items
            .Select(node => KubernetesMapper.ToMachine(node, nodeMetrics.GetValueOrDefault(node.Metadata.Name)))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var usage = podMetrics.Select(metrics =>
        {
            var pod = KubernetesMapper.ToUsage(metrics);
            return new WorkloadUsageSample($"{metrics.Metadata.Namespace}/{metrics.Metadata.Name}", pod.CpuCores, pod.MemoryBytes);
        }).ToList();

        var resolution = nodeMetricItems.Select(m => KubernetesMapper.ToWindow(m.Window)).Max();
        return new InfrastructureSample(machines, usage, resolution);
    }
}
