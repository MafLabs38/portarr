namespace Portarr.Core.Workloads;

/// <summary>
/// A running unit discovered by a connector (spec §4.1); never declared by hand. <c>Namespace</c> and
/// <c>Node</c> are set by orchestrators (Kubernetes): the pod's namespace and the node it runs on.
/// <c>Mounts</c> are its persistent storage (bind mounts, named volumes, PersistentVolumeClaims), which
/// tells where its data lives. <c>ImageDigest</c> is the registry digest of the running
/// image when the runtime reports it (Kubernetes' <c>imageID</c>): it tells whether a floating tag moved (V4).
/// </summary>
public sealed record Workload(
    string EnvironmentId,
    string Id,
    string Name,
    string Image,
    WorkloadState State,
    WorkloadHealth Health,
    string StatusText,
    DateTimeOffset CreatedAt,
    string? ComposeProject,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<PublishedPort>? Ports = null,
    string? Namespace = null,
    string? Node = null,
    IReadOnlyList<WorkloadMount>? Mounts = null,
    string? ImageDigest = null)
{
    public IReadOnlyList<PublishedPort> PublishedPorts => Ports ?? [];

    public IReadOnlyList<WorkloadMount> StorageMounts => Mounts ?? [];
}

/// <summary>
/// Persistent storage mounted into a workload. <c>Source</c> is the host path (bind, host path), the volume
/// name or the claim name; <c>Destination</c> the path inside the container. <c>CapacityBytes</c> is the size
/// requested for a claim, when the connector knows it (a StatefulSet's volume claim template).
/// </summary>
public sealed record WorkloadMount(MountKind Kind, string Source, string Destination, long? CapacityBytes = null);

public enum MountKind
{
    /// <summary>A host directory or file (<c>-v /srv/app:/config</c>).</summary>
    Bind,

    /// <summary>A named Docker volume.</summary>
    Volume,

    /// <summary>A Kubernetes PersistentVolumeClaim.</summary>
    Claim,

    /// <summary>A Kubernetes hostPath volume.</summary>
    HostPath,
}

/// <summary>A container port published on the host (<c>-p 8080:80</c>: public 8080, private 80).</summary>
public sealed record PublishedPort(int PrivatePort, int PublicPort, string Protocol);

public enum WorkloadState
{
    /// <summary>A state this version of Portarr does not know.</summary>
    Unknown,
    Created,
    Running,
    Paused,
    Restarting,
    Removing,
    Exited,
    Dead,
}

/// <summary>The workload's own healthcheck, when it defines one.</summary>
public enum WorkloadHealth
{
    None,
    Starting,
    Healthy,
    Unhealthy,
}

/// <summary>Live resource usage in the units shared by every environment (D-33).</summary>
public sealed record WorkloadUsage(double CpuCores, long MemoryBytes, long? MemoryLimitBytes);

public enum LogStream
{
    Stdout,
    Stderr,
}

/// <summary>A log line; <c>Truncated</c> when it was longer than the line limit.</summary>
public sealed record LogLine(DateTimeOffset? Timestamp, LogStream Stream, string Text, bool Truncated = false);
