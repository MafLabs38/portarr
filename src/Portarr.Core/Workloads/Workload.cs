namespace Portarr.Core.Workloads;

/// <summary>A running unit discovered by a connector (spec §4.1); never declared by hand.</summary>
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
    IReadOnlyDictionary<string, string> Labels);

public enum WorkloadState
{
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

public sealed record LogLine(DateTimeOffset? Timestamp, LogStream Stream, string Text);
