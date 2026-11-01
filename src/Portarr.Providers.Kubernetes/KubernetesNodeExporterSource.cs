using System.Collections.Concurrent;
using System.Net;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Providers.NodeExporter;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// A node's node_exporter read through the API server's <b>pod proxy</b> (<c>kubernetes.nodeExporter</c> with
/// <c>api-server</c>, D-72): for nodes Portarr cannot reach on their InternalIP (a worker behind a tunnel).
/// The exporter's pod on the node is found once (pods of <c>namespace</c> matching <c>selector</c>, running on
/// the node: listing pods is already allowed), then GET <c>/api/v1/namespaces/&lt;ns&gt;/pods/&lt;pod&gt;:&lt;port&gt;/proxy/metrics</c>
/// needs <c>get</c> on <c>pods/proxy</c> in that namespace only. The node proxy (<c>nodes/proxy</c>) would
/// reach any port of any node, kubelet included. Without the permission the API server answers 403 and the
/// node keeps metrics-server's values, with the reason.
/// </summary>
public sealed class KubernetesNodeExporterSource : IHostMetricsSource
{
    /// <summary>A pod name is looked up again after this long, even while it answers (a rollout renames the pods).</summary>
    public static readonly TimeSpan PodLifetime = TimeSpan.FromMinutes(5);

    private readonly KubernetesApiClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly NodeExporterSampler _sampler = new();
    private readonly ConcurrentDictionary<HostMachineKey, CachedPod> _pods = new();

    /// <summary>The nodes of a cluster that declares <c>kubernetes.nodeExporter</c>, each with its route (D-72, D-105).</summary>
    public IEnumerable<HostMetricsTarget> TargetsOf(CatalogEnvironment environment, EnvironmentMetrics? infrastructure)
    {
        return environment.Kubernetes()?.NodeExporter is { } nodeExporter ? KubernetesNodeTargets.Of(environment, nodeExporter, infrastructure) : [];
    }

    public KubernetesNodeExporterSource(KubernetesApiClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
    }

    public bool Supports(HostMetricsTarget target)
    {
        return target.Url is null && target.Route is PodProxyRoute && target.Environment.Kubernetes() is not null;
    }

    public async Task<HostReading> ReadAsync(HostMetricsTarget target, CancellationToken cancellationToken)
    {
        var cluster = target.Environment.Kubernetes() ?? throw new InvalidOperationException($"Environment {target.Environment.Id} has no Kubernetes cluster.");
        var exporter = cluster.NodeExporter ?? throw new InvalidOperationException($"Environment {target.Environment.Id} has no kubernetes.nodeExporter.");

        var cached = _pods.TryGetValue(target.Key, out var known) && _timeProvider.GetUtcNow() - known.At < PodLifetime ? known.Name : null;
        var pod = cached ?? await FindPodAsync(target, cluster, exporter, cancellationToken);
        try
        {
            return await ReadPodAsync(target, cluster, exporter, pod, cancellationToken);
        }
        catch (HttpRequestException exception) when (cached is not null && exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable)
        {
            // The pod was replaced since the lookup: find the new one once, and fail with what it answers.
            _pods.TryRemove(target.Key, out _);
            pod = await FindPodAsync(target, cluster, exporter, cancellationToken);
            return await ReadPodAsync(target, cluster, exporter, pod, cancellationToken);
        }
    }

    private async Task<HostReading> ReadPodAsync(HostMetricsTarget target, KubernetesCluster cluster, KubernetesNodeExporter exporter, string pod, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetResponseAsync(cluster, ProxyPath(exporter.Namespace, pod, ((PodProxyRoute)target.Route!).Port), "text/plain", cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await _sampler.ReadAsync(target.Key, stream, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException($"The token needs get on pods/proxy in the namespace {exporter.Namespace}.", exception, HttpStatusCode.Forbidden);
        }
    }

    /// <summary>The running node_exporter pod on the node (the first by name, so two pods do not alternate), remembered for <see cref="PodLifetime"/>.</summary>
    private async Task<string> FindPodAsync(HostMetricsTarget target, KubernetesCluster cluster, KubernetesNodeExporter exporter, CancellationToken cancellationToken)
    {
        var path = PodsPath(exporter.Namespace, exporter.Selector, target.MachineId);
        var pods = await _client.GetAsync(cluster, path, KubernetesJsonContext.Default.PodList, cancellationToken);
        var pod = pods?.Items
            .Where(p => p.Metadata.DeletionTimestamp is null)
            .Select(p => p.Metadata.Name)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (pod is null)
        {
            throw new HttpRequestException($"No running pod matching {exporter.Selector} in the namespace {exporter.Namespace} on node {target.MachineId}.", null, HttpStatusCode.NotFound);
        }

        _pods[target.Key] = new CachedPod(pod, _timeProvider.GetUtcNow());
        return pod;
    }

    /// <summary>Running pods of the namespace that match the label selector and run on the node.</summary>
    internal static string PodsPath(string podNamespace, string selector, string node)
    {
        return $"api/v1/namespaces/{Uri.EscapeDataString(podNamespace)}/pods?labelSelector={Uri.EscapeDataString(selector)}"
            + $"&fieldSelector={Uri.EscapeDataString($"spec.nodeName={node},status.phase=Running")}";
    }

    internal static string ProxyPath(string podNamespace, string pod, int? port)
    {
        return $"api/v1/namespaces/{Uri.EscapeDataString(podNamespace)}/pods/{Uri.EscapeDataString(pod)}:{port}/proxy/metrics";
    }

    private readonly record struct CachedPod(string Name, DateTimeOffset At);
}
