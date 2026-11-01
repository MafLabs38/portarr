using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Portarr.Core.Catalog;

// YAML shape of catalog.yaml. Kept separate from the domain records so the file format can
// evolve (shorthands, defaults) without leaking nullable, loosely typed fields into the Core.
#pragma warning disable CA1002, CA2227 // Mutable lists are what the YAML deserializer fills.

internal sealed class CatalogDocument
{
    public List<EnvironmentDocument>? Environments { get; set; }

    public List<StackDocument>? Stacks { get; set; }

    public List<AppDocument>? Apps { get; set; }

    public List<DataDocument>? Data { get; set; }

    public SourcesDocument? Sources { get; set; }

    /// <summary>Network links between environments and machines (D-84).</summary>
    public List<LinkDocument>? Links { get; set; }

    /// <summary>External providers and accounts (D-84).</summary>
    public List<ProviderDocument>? Providers { get; set; }

    /// <summary>The name and logo of the portal (D-91).</summary>
    public PortalDocument? Portal { get; set; }
}

/// <summary>See <see cref="CatalogPortal"/>.</summary>
internal sealed class PortalDocument
{
    public string? Name { get; set; }

    /// <summary>Relative to the catalog folder, e.g. <c>branding/logo.png</c>.</summary>
    public string? Logo { get; set; }

    public PortalColorsDocument? Colors { get; set; }
}

/// <summary>See <see cref="CatalogPortalColors"/>.</summary>
internal sealed class PortalColorsDocument
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

/// <summary>See <see cref="CatalogLink"/>.</summary>
internal sealed class LinkDocument
{
    public string? Id { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? Kind { get; set; }

    public string? Name { get; set; }

    /// <summary>The link's address range, e.g. 10.0.0.0/24.</summary>
    public string? Network { get; set; }

    public string? Notes { get; set; }
}

/// <summary>See <see cref="CatalogProvider"/>.</summary>
internal sealed class ProviderDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Kind { get; set; }

    public string? Console { get; set; }

    /// <summary>Which account: a name or an e-mail hint, never a credential.</summary>
    public string? Account { get; set; }

    /// <summary>Ids of the apps and environments that depend on it.</summary>
    public List<string>? UsedBy { get; set; }

    public string? Notes { get; set; }
}

/// <summary>See <see cref="MachineAccess"/>.</summary>
internal sealed class AccessDocument
{
    public SshDocument? Ssh { get; set; }

    public List<UrlDocument>? Consoles { get; set; }

    public string? Notes { get; set; }
}

internal sealed class SshDocument
{
    /// <summary>The alias of the operator's SSH config (<c>ssh server-1</c>), when they use one.</summary>
    public string? Alias { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? User { get; set; }

    /// <summary>The key file's name or path, never its content.</summary>
    public string? Key { get; set; }

    /// <summary>The jump host (ProxyJump), by alias or name.</summary>
    public string? Jump { get; set; }
}

/// <summary>See <see cref="MachineKnowledge"/>.</summary>
internal sealed class MachineDocument
{
    public string? Name { get; set; }

    public string? Notes { get; set; }

    public AccessDocument? Access { get; set; }
}

/// <summary>Code sources scanned by the Maintenance page (D-64).</summary>
internal sealed class SourcesDocument
{
    [YamlDotNet.Serialization.YamlMember(Alias = "github")]
    public GitHubDocument? GitHub { get; set; }
}

/// <summary>See <see cref="GitHubSource"/>.</summary>
internal sealed class GitHubDocument
{
    public List<string>? Owners { get; set; }

    /// <summary>Name of the environment variable holding the token (never the token itself).</summary>
    public string? TokenEnv { get; set; }

    public int? PushedWithinMonths { get; set; }

    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }

    public bool? Forks { get; set; }

    public bool? Archived { get; set; }
}

/// <summary>A followed data asset (D-77); see <see cref="DataAssetDefinition"/>.</summary>
internal sealed class DataDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>The id of the app this data belongs to.</summary>
    public string? App { get; set; }

    public string? Kind { get; set; }

    public DataWhereDocument? Where { get; set; }

    /// <summary>critical, important or comfort.</summary>
    public string? Importance { get; set; }

    /// <summary>The accepted loss window (<c>30m</c>, <c>24h</c>, <c>7d</c>); the importance sets a default.</summary>
    public string? MaxAge { get; set; }

    /// <summary>Per target (<c>local</c>, <c>offsite</c>): the sources that should prove the copy exists. A key with no value declares a copy nothing produces yet.</summary>
    public Dictionary<string, TargetDocument?>? Targets { get; set; }
}

internal sealed class DataWhereDocument
{
    public string? Environment { get; set; }

    public string? Namespace { get; set; }

    /// <summary>Container, pod or StatefulSet name.</summary>
    public string? Workload { get; set; }

    public string? Claim { get; set; }

    public string? Path { get; set; }
}

/// <summary>The sources of one target: any of them proving a recent copy is enough.</summary>
internal sealed class TargetDocument
{
    public BackrestSourceDocument? Backrest { get; set; }

    public K8UpSourceDocument? K8up { get; set; }

    /// <summary><c>namespace/name</c> of a Kubernetes CronJob.</summary>
    public string? Cronjob { get; set; }

    /// <summary>Your own copy, outside any tool: <c>true</c>, or the date of the last one (ISO 8601).</summary>
    public string? Manual { get; set; }
}

internal sealed class BackrestSourceDocument
{
    public string? Plan { get; set; }

    /// <summary>The environment running Backrest; the data's own environment when omitted.</summary>
    public string? Environment { get; set; }
}

internal sealed class K8UpSourceDocument
{
    /// <summary><c>namespace/name</c> of the K8up Schedule.</summary>
    public string? Schedule { get; set; }

    /// <summary>Narrows the schedule to this data: a part of the snapshot path (the claim name).</summary>
    public string? Item { get; set; }

    public string? Environment { get; set; }
}

internal sealed class EnvironmentDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>docker, cloudflare or kubernetes; inferred from the connector block when omitted.</summary>
    public string? Runtime { get; set; }

    /// <summary>The Portarr reader of this machine, in place of <c>docker</c> and <c>hostMetrics</c> (D-100, D-101).</summary>
    public ReaderDocument? Reader { get; set; }

    public KubernetesDocument? Kubernetes { get; set; }

    public string? Address { get; set; }

    /// <summary>The environment this machine is a VM on (D-59): its hypervisor counts it as a guest.</summary>
    public string? Host { get; set; }

    /// <summary>The sections of connector libraries (D-105), set aside as their events: each connector reads its own.</summary>
    [YamlIgnore]
    public IReadOnlyDictionary<string, IReadOnlyList<ParsingEvent>> Sections { get; set; } = new Dictionary<string, IReadOnlyList<ParsingEvent>>();

    /// <summary>Free Markdown for people and AI assistants (D-84).</summary>
    public string? Notes { get; set; }

    /// <summary>How to reach this machine (D-84), never a secret.</summary>
    public AccessDocument? Access { get; set; }

    /// <summary>Knowledge about the machines of this environment, e.g. the nodes of a cluster (D-84).</summary>
    public List<MachineDocument>? Machines { get; set; }
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
}

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

/// <summary>
/// The Portarr reader of a machine (D-100): one URL and the variable holding its token, read as <c>docker:</c> and
/// <c>hostMetrics:</c> (D-101).
/// </summary>
internal sealed class ReaderDocument
{
    public string? Url { get; set; }

    public string? TokenEnv { get; set; }
}

internal sealed class StackDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public List<MemberDocument>? Members { get; set; }

    public string? Notes { get; set; }
}

internal sealed class MemberDocument
{
    public string? App { get; set; }

    public string? Role { get; set; }
}

internal sealed class AppDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Icon { get; set; }

    public string? Description { get; set; }

    /// <summary>Shorthand for a critical membership in that stack.</summary>
    public string? Stack { get; set; }

    public List<string>? Tags { get; set; }

    public List<UrlDocument>? Urls { get; set; }

    public List<SelectorDocument>? Workloads { get; set; }

    /// <summary><c>false</c>, <c>true</c> or a mapping (see <see cref="CheckBodyDocument"/>).</summary>
    public CheckDocument? Check { get; set; }

    /// <summary>The app's code repository, <c>owner/name</c> (D-65).</summary>
    public string? Repository { get; set; }

    public string? Notes { get; set; }
}

/// <summary><c>check:</c> is either a boolean or a mapping; read by <see cref="CheckDocumentConverter"/>.</summary>
internal sealed class CheckDocument
{
    /// <summary>The scalar as written when it is not a boolean, for the error message.</summary>
    public string? InvalidScalar { get; init; }

    public bool Enabled { get; init; } = true;

    public CheckBodyDocument? Body { get; init; }
}

internal sealed class CheckBodyDocument
{
    public string? Url { get; set; }

    /// <summary>Status codes (<c>200</c>) or ranges (<c>200-299</c>).</summary>
    public List<string>? Expect { get; set; }

    public string? Keyword { get; set; }

    /// <summary>Seconds (<c>60</c>) or a duration (<c>30s</c>, <c>5m</c>).</summary>
    public string? Interval { get; set; }

    public string? Timeout { get; set; }

    public bool? Critical { get; set; }
}

internal sealed class UrlDocument
{
    public string? Label { get; set; }

    public string? Href { get; set; }

    /// <summary>public or lan; inferred from the host when omitted.</summary>
    public string? Scope { get; set; }
}

internal sealed class SelectorDocument
{
    /// <summary><c>docker@&lt;environment id&gt;</c>, <c>cloudflare@…</c> or <c>kubernetes@…</c>.</summary>
    public string? Runtime { get; set; }

    public MatchDocument? Match { get; set; }

    public string? Role { get; set; }
}

internal sealed class MatchDocument
{
    public string? Name { get; set; }

    /// <summary>Kubernetes only: the pod's namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Kubernetes only: the pod's <c>app</c> label.</summary>
    public string? App { get; set; }

    /// <summary>Kubernetes only: the start of the pod name (a Deployment's pods are <c>&lt;name&gt;-&lt;hash&gt;-&lt;id&gt;</c>).</summary>
    public string? NamePrefix { get; set; }
}
