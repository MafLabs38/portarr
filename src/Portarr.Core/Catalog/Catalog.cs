using Portarr.Core.Workloads;

namespace Portarr.Core.Catalog;

/// <summary>
/// What the operator declared: environments, stacks and apps (spec §4), the data followed (D-77) and the code
/// sources the Maintenance page scans (D-64). Workloads are never declared; apps point at them through selectors.
/// </summary>
public sealed record Catalog(
    IReadOnlyList<CatalogEnvironment> Environments,
    IReadOnlyList<CatalogStack> Stacks,
    IReadOnlyList<CatalogApp> Apps,
    IReadOnlyList<DataAssetDefinition>? Data = null,
    GitHubSource? GitHub = null,
    IReadOnlyList<CatalogLink>? Links = null,
    IReadOnlyList<CatalogProvider>? Providers = null,
    CatalogPortal? Portal = null)
{
    public static Catalog Empty { get; } = new([], [], []);

    public IReadOnlyList<DataAssetDefinition> DataAssets => Data ?? [];

    /// <summary>Network links between environments and machines (D-84).</summary>
    public IReadOnlyList<CatalogLink> NetworkLinks => Links ?? [];

    /// <summary>External providers and accounts (D-84).</summary>
    public IReadOnlyList<CatalogProvider> ExternalProviders => Providers ?? [];
}

/// <summary>
/// The identity of the portal (D-91): the name of the infrastructure and a logo, shown in the sidebar. <c>Logo</c> is
/// a path relative to the catalog folder (<c>branding/logo.png</c>); all optional, Portarr's own are the defaults.
/// </summary>
public sealed record CatalogPortal(string? Name, string? Logo, CatalogPortalColors? Colors = null);

/// <summary>The colors of the badge texts: a hex color or a theme token name (see <c>PortalColor</c>); each one optional, Portarr's accent is the default.</summary>
public sealed record CatalogPortalColors(string? Name, string? Version);

/// <summary>
/// A host, a cluster or a hosting account. <c>Address</c> is its LAN address, used to build LAN URLs from
/// published ports (D-46). At most one connector endpoint is set: <c>Docker</c>, <c>Cloudflare</c> or <c>Kubernetes</c>.
/// <c>Host</c> is the environment this one runs on as a VM (D-59): the hypervisor's dashboard counts it as a guest.
/// <c>HostMetrics</c> is the host agent of its single machine (a Docker host, D-72). <c>Backrest</c> is the
/// backup tool running on it (D-80). <c>Notes</c>, <c>Access</c> and <c>Machines</c> are what the operator knows
/// about it, for people and AI assistants (D-84).
/// </summary>
public sealed record CatalogEnvironment(
    string Id,
    string Name,
    DockerEndpoint? Docker,
    string? Address = null,
    CloudflareAccount? Cloudflare = null,
    KubernetesCluster? Kubernetes = null,
    string? Host = null,
    HostMetricsEndpoint? HostMetrics = null,
    BackrestEndpoint? Backrest = null,
    string? Notes = null,
    MachineAccess? Access = null,
    IReadOnlyList<MachineKnowledge>? Machines = null)
{
    /// <summary>
    /// The environment a machine of this environment is a VM on: its node's entry in <c>nodeHosts</c>, else the
    /// environment's <c>host</c>; null for a physical machine or a VPS whose hypervisor is not in the catalog.
    /// </summary>
    public string? HostOf(string machineId)
    {
        return Kubernetes?.NodeHosts?.GetValueOrDefault(machineId) ?? Host;
    }

    /// <summary>The connector kind: docker, cloudflare, kubernetes, or none (a launcher-only environment, D-32).</summary>
    public string Runtime => (Docker, Cloudflare, Kubernetes) switch
    {
        ({ }, _, _) => RuntimeNames.Docker,
        (_, { }, _) => RuntimeNames.Cloudflare,
        (_, _, { }) => RuntimeNames.Kubernetes,
        _ => RuntimeNames.None,
    };
}

public static class RuntimeNames
{
    public const string Docker = "docker";
    public const string Cloudflare = "cloudflare";
    public const string Kubernetes = "kubernetes";
    public const string None = "none";
}

/// <summary>
/// A Kubernetes API server (D-55). <c>TokenVariable</c> names the environment variable holding a read-only
/// ServiceAccount token, never the token itself. <c>CaFile</c> is the cluster CA certificate (PEM), relative
/// to the catalog folder or absolute; without it the system trust store is used. <c>NodeHosts</c> maps a node
/// name to the environment the node is a VM on (D-59). <c>NodeExporter</c> says where each node's node_exporter is read (D-72).
/// </summary>
public sealed record KubernetesCluster(
    Uri Server,
    string TokenVariable,
    string? CaFile,
    IReadOnlyDictionary<string, string>? NodeHosts = null,
    KubernetesNodeExporter? NodeExporter = null);

/// <summary>
/// The node_exporter of a single machine (D-72): its <c>/metrics</c> URL, read every second. <c>TokenVariable</c> names the
/// environment variable holding a bearer token when something in front of it asks for one (the Portarr reader does, D-100).
/// </summary>
public sealed record HostMetricsEndpoint(Uri NodeExporter, string? TokenVariable = null);

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
    string Selector = KubernetesNodeExporter.DefaultSelector)
{
    public const int DefaultPort = 9100;
    public const string DefaultNamespace = "monitoring";
    public const string DefaultSelector = "app.kubernetes.io/name=node-exporter";

    public NodeExporterRoute RouteOf(string node)
    {
        return Nodes?.GetValueOrDefault(node) ?? new NodeExporterRoute(Access);
    }
}

/// <summary>
/// Docker Engine API endpoint: <c>unix:///var/run/docker.sock</c> or <c>http://host:2375</c> (socket proxy). <c>TokenVariable</c>
/// names the environment variable holding a bearer token when something in front of Docker asks for one (the Portarr reader
/// does, D-100): never the token itself.
/// </summary>
public sealed record DockerEndpoint(Uri Url, string? TokenVariable = null);

/// <summary>
/// A Cloudflare account (D-53). <c>TokenVariable</c> names the environment variable holding the API
/// token: the token itself is never written in the catalog.
/// </summary>
public sealed record CloudflareAccount(string AccountId, string TokenVariable);

public sealed record CatalogStack(string Id, string Name, string? Description, IReadOnlyList<StackMember> Members, string? Notes = null);

public sealed record StackMember(string AppId, MemberRole Role);

public enum MemberRole
{
    Critical,
    Optional,
}

/// <summary>
/// An app of the catalog. <c>Repository</c> is its code repository (<c>owner/name</c>), when declared (D-65).
/// <c>Notes</c> is free Markdown for people and AI assistants (D-84).
/// </summary>
public sealed record CatalogApp(
    string Id,
    string Name,
    AppType Type,
    string? Icon,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AppUrl> Urls,
    IReadOnlyList<WorkloadSelector> Workloads,
    CheckDefinition? Check = null,
    string? Repository = null,
    string? Notes = null);

public enum AppType
{
    Web,
    Desktop,
    Service,
}

public sealed record AppUrl(string Label, Uri Href, UrlScope Scope);

/// <summary>Public URLs go through DNS; LAN URLs only work from the local network (D-46).</summary>
public enum UrlScope
{
    Public,
    Lan,
}

/// <summary>
/// Matches discovered workloads of one environment: by container, project or pod name, or, on Kubernetes,
/// by namespace, <c>app</c> label and name prefix, which stay stable when a rollout renames the pods (D-56).
/// Every criterion given must match. An <c>Optional</c> role means the app still works without it: its
/// failure only degrades the app (D-48).
/// </summary>
public sealed record WorkloadSelector(
    string EnvironmentId,
    string? Name,
    MemberRole Role = MemberRole.Critical,
    string? Namespace = null,
    string? App = null,
    string? NamePrefix = null)
{
    /// <summary>The label a Kubernetes selector matches with <c>app:</c>.</summary>
    public const string AppLabel = "app";

    /// <summary>What the selector looks for, for humans: "jellyfin", "media/app=jellyfin", "media/web-*".</summary>
    public string Description
    {
        get
        {
            var target = Name ?? (App is not null ? $"{AppLabel}={App}" : $"{NamePrefix}*");
            return Namespace is null ? target : $"{Namespace}/{target}";
        }
    }

    public bool Matches(Workload workload)
    {
        return workload.EnvironmentId == EnvironmentId
            && (Name is null || workload.Name == Name)
            && (NamePrefix is null || workload.Name.StartsWith(NamePrefix, StringComparison.Ordinal))
            && (Namespace is null || workload.Namespace == Namespace)
            && (App is null || (workload.Labels.TryGetValue(AppLabel, out var app) && app == App));
    }

    /// <summary>
    /// The workload an app is linked to: a selector by label can match several pods (replicas, a rollout in
    /// progress); the one that serves best is shown (running and ready first, then the newest).
    /// </summary>
    public Workload? Pick(IEnumerable<Workload> workloads)
    {
        return workloads.Where(Matches)
            .OrderByDescending(w => w.State == WorkloadState.Running)
            .ThenByDescending(w => w.Health is WorkloadHealth.Healthy or WorkloadHealth.None)
            .ThenByDescending(w => w.CreatedAt)
            .FirstOrDefault();
    }
}
