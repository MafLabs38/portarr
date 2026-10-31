namespace Portarr.Core.Catalog;

// YAML shape of catalog.yaml. Kept separate from the domain records so the file format can
// evolve (shorthands, defaults) without leaking nullable, loosely typed fields into the Core.
#pragma warning disable CA1002, CA2227 // Mutable lists are what the YAML deserializer fills.

internal sealed class CatalogDocument
{
    public List<EnvironmentDocument>? Environments { get; set; }

    public List<StackDocument>? Stacks { get; set; }

    public List<AppDocument>? Apps { get; set; }
}

internal sealed class EnvironmentDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>docker, cloudflare or kubernetes; inferred from the connector block when omitted.</summary>
    public string? Runtime { get; set; }

    public DockerDocument? Docker { get; set; }

    public CloudflareDocument? Cloudflare { get; set; }

    public KubernetesDocument? Kubernetes { get; set; }

    public string? Address { get; set; }

    /// <summary>The environment this machine is a VM on (D-59): its hypervisor counts it as a guest.</summary>
    public string? Host { get; set; }

    /// <summary>The host agent of a Docker host (D-72).</summary>
    public HostMetricsDocument? HostMetrics { get; set; }
}

internal sealed class HostMetricsDocument
{
    /// <summary>node_exporter's metrics URL, e.g. http://10.0.0.10:9100/metrics.</summary>
    public string? NodeExporter { get; set; }
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

internal sealed class CloudflareDocument
{
    public string? AccountId { get; set; }

    /// <summary>Name of the environment variable holding the API token (never the token itself).</summary>
    public string? TokenEnv { get; set; }
}

internal sealed class KubernetesDocument
{
    public string? Server { get; set; }

    /// <summary>Name of the environment variable holding the ServiceAccount token (never the token itself).</summary>
    public string? TokenEnv { get; set; }

    /// <summary>Cluster CA certificate (PEM), relative to the catalog folder.</summary>
    public string? CaFile { get; set; }

    /// <summary>Per node name, the environment the node is a VM on (D-59); overrides the environment's <c>host</c>.</summary>
    public Dictionary<string, string>? NodeHosts { get; set; }

    /// <summary>node_exporter on the nodes (D-72).</summary>
    public NodeExporterDocument? NodeExporter { get; set; }
}

internal sealed class DockerDocument
{
    public string? Url { get; set; }
}

internal sealed class StackDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public List<MemberDocument>? Members { get; set; }
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
