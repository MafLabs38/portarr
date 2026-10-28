namespace Portarr.Core.Catalog;

/// <summary>
/// What the operator declared: environments, stacks and apps (spec §4). Workloads are never
/// declared; apps point at them through selectors.
/// </summary>
public sealed record Catalog(
    IReadOnlyList<CatalogEnvironment> Environments,
    IReadOnlyList<CatalogStack> Stacks,
    IReadOnlyList<CatalogApp> Apps)
{
    public static Catalog Empty { get; } = new([], [], []);
}

public sealed record CatalogEnvironment(string Id, string Name, DockerEndpoint? Docker);

/// <summary>Docker Engine API endpoint: <c>unix:///var/run/docker.sock</c> or <c>http://host:2375</c> (socket proxy).</summary>
public sealed record DockerEndpoint(Uri Url);

public sealed record CatalogStack(string Id, string Name, string? Description, IReadOnlyList<StackMember> Members);

public sealed record StackMember(string AppId, MemberRole Role);

public enum MemberRole
{
    Critical,
    Optional,
}

public sealed record CatalogApp(
    string Id,
    string Name,
    AppType Type,
    string? Icon,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AppUrl> Urls,
    IReadOnlyList<WorkloadSelector> Workloads);

public enum AppType
{
    Web,
    Desktop,
    Service,
}

public sealed record AppUrl(string Label, Uri Href);

/// <summary>Matches discovered workloads of one environment by container name.</summary>
public sealed record WorkloadSelector(string EnvironmentId, string Name);
