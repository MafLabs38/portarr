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
    IReadOnlyList<CodeSource>? Sources = null,
    IReadOnlyList<CatalogLink>? Links = null,
    IReadOnlyList<CatalogProvider>? Providers = null,
    CatalogPortal? Portal = null)
{
    public static Catalog Empty { get; } = new([], [], []);

    public IReadOnlyList<DataAssetDefinition> DataAssets => Data ?? [];

    /// <summary>The code hosts whose repositories the Maintenance page scans (D-64), one entry per account.</summary>
    public IReadOnlyList<CodeSource> CodeSources => Sources ?? [];

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
/// published ports (D-46). The runtime comes from a connector library (<c>ConnectorRuntime</c>, D-105); <c>SingleMachine</c> says whether the environment is one machine (a Docker host) or many (a cluster, an account).
/// <c>Host</c> is the environment this one runs on as a VM (D-59): the hypervisor's dashboard counts it as a guest.
/// <c>Notes</c>, <c>Access</c> and
/// <c>Machines</c> are what the operator knows about it, for people and AI assistants (D-84). <c>Connectors</c> holds the
/// settings each connector library read from the section it owns, keyed by that section (D-105): Core never looks inside.
/// <c>MachineHosts</c> says which machines of the environment are VMs on another environment, as those connectors know them.
/// </summary>
public sealed record CatalogEnvironment(
    string Id,
    string Name,
    string? Address = null,
    string? Host = null,
    string? Notes = null,
    MachineAccess? Access = null,
    IReadOnlyList<MachineKnowledge>? Machines = null,
    IReadOnlyDictionary<string, object>? Connectors = null,
    IReadOnlyDictionary<string, string>? MachineHosts = null,
    string? ConnectorRuntime = null,
    bool SingleMachine = true)
{
    /// <summary>Whether this environment declares the section a connector owns.</summary>
    public bool Declares(string section) => Connectors?.ContainsKey(section) == true;

    /// <summary>The settings a connector read from its section of this environment, or null when the environment does not declare it.</summary>
    public T? Settings<T>(string section)
        where T : class
    {
        if (Connectors is null || !Connectors.TryGetValue(section, out var settings))
        {
            return null;
        }

        return settings as T ?? throw new InvalidOperationException($"The settings of '{section}' in environment '{Id}' are a {settings.GetType().Name}, not a {typeof(T).Name}.");
    }

    /// <summary>
    /// The environment a machine of this environment is a VM on: its node's entry in <c>nodeHosts</c>, else the
    /// environment's <c>host</c>; null for a physical machine or a VPS whose hypervisor is not in the catalog.
    /// </summary>
    public string? HostOf(string machineId)
    {
        return MachineHosts?.GetValueOrDefault(machineId) ?? Host;
    }

    /// <summary>The connector kind: docker, cloudflare, kubernetes, or none (a launcher-only environment, D-32).</summary>
    public string Runtime => ConnectorRuntime ?? RuntimeNames.None;
}

public static class RuntimeNames
{
    public const string Docker = "docker";
    public const string Cloudflare = "cloudflare";
    public const string Kubernetes = "kubernetes";
    public const string None = "none";
}

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
/// by namespace, labels (<c>app</c> is the shorthand of the <c>app</c> label) and name prefix, which stay stable when a
/// rollout renames the pods (D-56, D-106).
/// Every criterion given must match. An <c>Optional</c> role means the app still works without it: its
/// failure only degrades the app (D-48).
/// </summary>
public sealed record WorkloadSelector(
    string EnvironmentId,
    string? Name,
    MemberRole Role = MemberRole.Critical,
    string? Namespace = null,
    string? App = null,
    string? NamePrefix = null,
    IReadOnlyDictionary<string, string>? Labels = null)
{
    /// <summary>The label a Kubernetes selector matches with <c>app:</c>.</summary>
    public const string AppLabel = "app";

    /// <summary>What the selector looks for, for humans: "jellyfin", "media/app=jellyfin", "media/web-*".</summary>
    public string Description
    {
        get
        {
            var labels = LabelCriteria();
            var target = Name ?? (labels.Count > 0 ? string.Join(',', labels.Select(label => $"{label.Key}={label.Value}")) : $"{NamePrefix}*");
            return Namespace is null ? target : $"{Namespace}/{target}";
        }
    }

    /// <summary>Every label the pod must carry: <c>app</c> (the shorthand) and the <c>labels</c>, by key.</summary>
    private SortedDictionary<string, string> LabelCriteria()
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in Labels ?? new Dictionary<string, string>())
        {
            all[key] = value;
        }
        if (App is not null)
        {
            all[AppLabel] = App;
        }

        return all;
    }

    public bool Matches(Workload workload)
    {
        return workload.EnvironmentId == EnvironmentId
            && (Name is null || workload.Name == Name)
            && (NamePrefix is null || workload.Name.StartsWith(NamePrefix, StringComparison.Ordinal))
            && (Namespace is null || workload.Namespace == Namespace)
            && LabelCriteria().All(label => workload.Labels.TryGetValue(label.Key, out var value) && value == label.Value);
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
