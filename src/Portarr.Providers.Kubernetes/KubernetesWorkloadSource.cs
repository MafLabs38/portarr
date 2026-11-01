using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// Lists the pods of every namespace as workloads (D-55) and reads a pod's usage from metrics.k8s.io.
/// Logs are not served yet. The ServiceAccount needs <c>get, list</c> on <c>pods</c> and <c>nodes</c>, and on
/// <c>pods</c> and <c>nodes</c> of the <c>metrics.k8s.io</c> group. With <c>list</c> on <c>statefulsets</c>
/// (group <c>apps</c>), the size requested by StatefulSet claims is added to the pods' mounts; without it,
/// sizes are simply unknown.
/// </summary>
public sealed partial class KubernetesWorkloadSource : IWorkloadSource
{
    /// <summary>Pods per page; a homelab cluster fits in one or two pages.</summary>
    private const int PageSize = 500;

    private const int MaxPages = 20;

    private readonly KubernetesApiClient _client;

    public KubernetesWorkloadSource(KubernetesApiClient client)
    {
        _client = client;
    }

    public bool SupportsUsage => true;

    public bool SupportsLogs => false;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Kubernetes() is not null;
    }

    public string? GetConfigurationError(CatalogEnvironment environment)
    {
        return environment.Kubernetes() is { } cluster ? _client.GetConfigurationError(cluster) : null;
    }

    public async Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var cluster = Cluster(environment);
        var workloads = new List<Workload>();
        string? next = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var path = next is null ? $"api/v1/pods?limit={PageSize}" : $"api/v1/pods?limit={PageSize}&continue={Uri.EscapeDataString(next)}";
            var pods = await _client.GetAsync(cluster, path, KubernetesJsonContext.Default.PodList, cancellationToken)
                ?? throw new InvalidDataException("The pods endpoint was not found.");

            workloads.AddRange(pods.Items.Select(pod => KubernetesMapper.ToWorkload(environment.Id, pod)));
            next = pods.Metadata?.Continue;
            if (string.IsNullOrEmpty(next))
            {
                break;
            }
        }

        var capacities = await ReadClaimCapacitiesAsync(cluster, cancellationToken);
        return capacities.Count == 0 ? workloads : workloads.Select(workload => WithCapacities(workload, capacities)).ToList();
    }

    private static Workload WithCapacities(Workload workload, Dictionary<string, long> capacities)
    {
        if (!workload.StorageMounts.Any(m => m.Kind == MountKind.Claim))
        {
            return workload;
        }

        var mounts = workload.StorageMounts
            .Select(m => m.Kind == MountKind.Claim && capacities.TryGetValue($"{workload.Namespace}/{m.Source}", out var bytes) ? m with { CapacityBytes = bytes } : m)
            .ToList();
        return workload with { Mounts = mounts };
    }

    /// <summary>Optional: a cluster that refuses StatefulSets (RBAC) still lists its pods.</summary>
    private async Task<Dictionary<string, long>> ReadClaimCapacitiesAsync(KubernetesCluster cluster, CancellationToken cancellationToken)
    {
        try
        {
            var sets = await _client.GetAsync(cluster, "apis/apps/v1/statefulsets", KubernetesJsonContext.Default.StatefulSetList, cancellationToken);
            return sets is null ? [] : KubernetesMapper.ToClaimCapacities(sets);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return [];
        }
    }

    /// <summary>Pod names are only unique per namespace: usage is read through the listed workload.</summary>
    public Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        return Task.FromResult<WorkloadUsage?>(null);
    }

    public async Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, Workload workload, CancellationToken cancellationToken)
    {
        var ns = Valid(workload.Namespace ?? string.Empty);
        var name = Valid(workload.Name);
        var metrics = await _client.GetAsync(Cluster(environment), $"apis/metrics.k8s.io/v1beta1/namespaces/{ns}/pods/{name}", KubernetesJsonContext.Default.PodMetrics, cancellationToken);

        return metrics is null ? null : KubernetesMapper.ToUsage(metrics);
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        return Task.FromResult<ILogStream?>(null);
    }

    private static KubernetesCluster Cluster(CatalogEnvironment environment)
    {
        return environment.Kubernetes() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Kubernetes cluster.");
    }

    /// <summary>Namespaces and pod names go into URL paths: only DNS-1123 names are accepted.</summary>
    private static string Valid(string name)
    {
        return NamePattern().IsMatch(name) ? name : throw new ArgumentException($"'{name}' is not a valid Kubernetes name.", nameof(name));
    }

    [GeneratedRegex("^[a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?$")]
    private static partial Regex NamePattern();
}
