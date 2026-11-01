using Portarr.Core.Catalog;
using Portarr.Providers.NodeExporter;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// A Kubernetes API server (D-55). <c>TokenVariable</c> names the environment variable holding a read-only
/// ServiceAccount token, never the token itself. <c>CaFile</c> is the cluster CA certificate (PEM), relative
/// to the catalog folder or absolute; without it the system trust store is used. <c>NodeHosts</c> maps a node
/// name to the environment the node is a VM on (D-59). <c>NodeExporter</c> says where each node's node_exporter is read (D-72).
/// <c>AllowInsecure</c> accepts a plain-http <c>Server</c>: the ServiceAccount token then travels in clear, so it is for a lab or a test only.
/// </summary>
public sealed record KubernetesCluster(
    Uri Server,
    string TokenVariable,
    string? CaFile,
    IReadOnlyDictionary<string, string>? NodeHosts = null,
    KubernetesNodeExporter? NodeExporter = null,
    bool AllowInsecure = false);

/// <summary>How a node's node_exporter is reached (D-72).</summary>
public enum NodeExporterAccess
{
    /// <summary>At the node's InternalIP, on the configured port.</summary>
    Direct,

    /// <summary>
    /// Through the API server's pod proxy to the node_exporter pod running on the node (needs <c>get</c> on
    /// <c>pods/proxy</c> in the exporter's namespace only), for nodes Portarr cannot reach.
    /// </summary>
    ApiServer,

    /// <summary>At an explicit URL.</summary>
    Url,

    /// <summary>Not read: the node keeps metrics-server's values.</summary>
    Off,
}

/// <summary>One node's route to its node_exporter; <c>Url</c> only for <see cref="NodeExporterAccess.Url"/>.</summary>
public sealed record NodeExporterRoute(NodeExporterAccess Access, Uri? Url = null);

/// <summary>
/// node_exporter on the nodes of a cluster (D-72), usually a DaemonSet: every node is read with <c>Access</c>
/// (at its InternalIP on <c>Port</c>, or through the API server), unless <c>Nodes</c> says otherwise for it.
/// <c>Namespace</c> and <c>Selector</c> (a label selector) find the exporter's pod on a node for the API server access.
/// </summary>
public sealed record KubernetesNodeExporter(
    int Port,
    NodeExporterAccess Access,
    IReadOnlyDictionary<string, NodeExporterRoute>? Nodes = null,
    string Namespace = KubernetesNodeExporter.DefaultNamespace,
    string Selector = KubernetesNodeExporter.DefaultSelector,
    TimeSpan? Interval = null)
{
    public const int DefaultPort = 9100;
    public const string DefaultNamespace = "monitoring";
    public const string DefaultSelector = "app.kubernetes.io/name=node-exporter";

    public NodeExporterRoute RouteOf(string node)
    {
        return Nodes?.GetValueOrDefault(node) ?? new NodeExporterRoute(Access);
    }
}

/// <summary>The <c>kubernetes:</c> section of an environment (D-55).</summary>
internal sealed class KubernetesDocument
{
    public string? Server { get; set; }

    /// <summary>Name of the environment variable holding the ServiceAccount token (never the token itself).</summary>
    public string? TokenEnv { get; set; }

    /// <summary>Cluster CA certificate (PEM), relative to the catalog folder.</summary>
    public string? CaFile { get; set; }

    /// <summary>Accepts an <c>http://</c> API server (the token travels in clear): a lab or a test only.</summary>
    public bool AllowInsecure { get; set; }

    /// <summary>Per node name, the environment the node is a VM on (D-59); overrides the environment's <c>host</c>.</summary>
    public Dictionary<string, string>? NodeHosts { get; set; }

    /// <summary>node_exporter on the nodes (D-72).</summary>
    public NodeExporterDocument? NodeExporter { get; set; }
}

internal sealed class NodeExporterDocument
{
    /// <summary>node_exporter's port on every node (default 9100).</summary>
    public int? Port { get; set; }

    /// <summary>direct (the node's InternalIP, default) or api-server (the API server's pod proxy).</summary>
    public string? Access { get; set; }

    /// <summary>Namespace of the node_exporter DaemonSet, for api-server access (default monitoring).</summary>
    public string? Namespace { get; set; }

    /// <summary>Label selector of its pods, for api-server access (default app.kubernetes.io/name=node-exporter).</summary>
    public string? Selector { get; set; }

    /// <summary>Per node name: direct, api-server, off, or a metrics URL.</summary>
    public Dictionary<string, string>? Nodes { get; set; }

    /// <summary>How often the nodes are read when not every second: <c>5s</c>, <c>1m</c> (1 second to 5 minutes).</summary>
    public string? Interval { get; set; }
}

/// <summary>
/// <c>kubernetes:</c> declares a cluster, whose nodes are the machines and whose pods are the workloads (D-55, D-105). The
/// ServiceAccount token goes to the API server the catalog names, over https unless the section says <c>allowInsecure</c> (D-103).
/// </summary>
public sealed class KubernetesEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "kubernetes";

    public const string DefaultTokenVariable = "K8S_TOKEN";

    public string Section => SectionName;

    public bool IsRuntime => true;

    /// <summary>A cluster is many machines: it cannot be a VM host, carry a Backrest or a host agent.</summary>
    public bool SingleMachine => false;

    /// <summary>Pods are found by namespace, <c>app</c> label and name prefix as well as by name (D-56).</summary>
    public WorkloadMatchFields MatchFields => WorkloadMatchFields.All;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<KubernetesDocument>() is not { } cluster)
        {
            return null;
        }

        if (!Uri.TryCreate(cluster.Server, UriKind.Absolute, out var server) || server.Scheme is not ("https" or "http") || server.UserInfo.Length > 0)
        {
            context.Error("kubernetes.server must be the API server URL (e.g. https://10.0.0.10:6443), without credentials.");
            return null;
        }

        if (server.Scheme == "http" && !cluster.AllowInsecure)
        {
            context.Error("kubernetes.server must be https: the ServiceAccount token travels in it (kubernetes.allowInsecure: true accepts http, for a lab or a test only).");
            return null;
        }

        var variable = cluster.TokenEnv ?? DefaultTokenVariable;
        if (!CatalogValues.IsVariableName(variable))
        {
            context.Error($"kubernetes.tokenEnv must be the name of an environment variable (e.g. {DefaultTokenVariable}), never the token itself.");
            return null;
        }

        if (cluster.CaFile is { } caFile && string.IsNullOrWhiteSpace(caFile))
        {
            context.Error("kubernetes.caFile must be the path of the cluster CA certificate (relative to the catalog), or be omitted.");
            return null;
        }

        var nodeHosts = cluster.NodeHosts?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key.Trim(), entry => entry.Value.Trim(), StringComparer.Ordinal);
        if (nodeHosts is not null && nodeHosts.Count != cluster.NodeHosts!.Count)
        {
            context.Error("kubernetes.nodeHosts maps node names to environment ids (e.g. { node-1: server-1 }).");
            return null;
        }

        if (!TryReadNodeExporter(context, cluster.NodeExporter, out var nodeExporter))
        {
            return null;
        }

        var hosts = nodeHosts is { Count: > 0 } ? nodeHosts : null;
        return new EnvironmentConnectorResult(new KubernetesCluster(server, variable, cluster.CaFile?.Trim(), hosts, nodeExporter, cluster.AllowInsecure), hosts);
    }

    private static bool TryReadNodeExporter(EnvironmentSectionContext context, NodeExporterDocument? document, out KubernetesNodeExporter? nodeExporter)
    {
        nodeExporter = null;
        if (document is null)
        {
            return true;
        }

        var port = document.Port ?? KubernetesNodeExporter.DefaultPort;
        var access = ParseAccess(document.Access ?? "direct");
        if (port is < 1 or > 65535 || access is not (NodeExporterAccess.Direct or NodeExporterAccess.ApiServer))
        {
            context.Error($"kubernetes.nodeExporter needs a port (1-65535, default {KubernetesNodeExporter.DefaultPort}) and an access of direct or api-server.");
            return false;
        }

        var podNamespace = (document.Namespace ?? KubernetesNodeExporter.DefaultNamespace).Trim();
        var selector = (document.Selector ?? KubernetesNodeExporter.DefaultSelector).Trim();
        if (!CatalogValues.IsNamespaceName(podNamespace) || selector.Length is 0 or > 512)
        {
            context.Error($"kubernetes.nodeExporter.namespace must be a namespace name (default {KubernetesNodeExporter.DefaultNamespace}) and selector a pod label selector (default {KubernetesNodeExporter.DefaultSelector}).");
            return false;
        }

        var nodes = new Dictionary<string, NodeExporterRoute>(StringComparer.Ordinal);
        foreach (var (node, value) in document.Nodes ?? [])
        {
            NodeExporterRoute? route = null;
            if (ParseAccess(value) is { } nodeAccess)
            {
                route = new NodeExporterRoute(nodeAccess);
            }
            else if (CatalogValues.HttpUrl(value) is { } url)
            {
                route = new NodeExporterRoute(NodeExporterAccess.Url, url);
            }

            if (string.IsNullOrWhiteSpace(node) || route is null)
            {
                context.Error("kubernetes.nodeExporter.nodes maps a node name to direct, api-server, off or a metrics URL (e.g. { node-2: api-server }).");
                return false;
            }

            nodes[node.Trim()] = route;
        }

        TimeSpan? interval = null;
        if (document.Interval is not null && (interval = CatalogValues.ParseDuration(document.Interval, HostMetricsIntervals.Min, HostMetricsIntervals.Max)) is null)
        {
            context.Error("kubernetes.nodeExporter.interval must be a duration of 1 second to 5 minutes (e.g. 5s, 1m), or be omitted to read every second.");
            return false;
        }

        nodeExporter = new KubernetesNodeExporter(port, access.Value, nodes.Count > 0 ? nodes : null, podNamespace, selector, interval);
        return true;
    }

    private static NodeExporterAccess? ParseAccess(string? value) => value?.Trim() switch
    {
        "direct" => NodeExporterAccess.Direct,
        "api-server" => NodeExporterAccess.ApiServer,
        "off" => NodeExporterAccess.Off,
        _ => null,
    };
}

public static class KubernetesEnvironmentExtensions
{
    /// <summary>The cluster this environment declares, or null.</summary>
    public static KubernetesCluster? Kubernetes(this CatalogEnvironment environment)
    {
        return environment.Settings<KubernetesCluster>(KubernetesEnvironmentConnector.SectionName);
    }
}
