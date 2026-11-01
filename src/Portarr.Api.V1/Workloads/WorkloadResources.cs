using Portarr.Api.V1.Catalog;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Api.V1.Workloads;

/// <summary>
/// An environment. <c>Connection</c>: connected, unreachable, pending (not polled yet) or
/// not-connected (no connector declared, or declared but not configured yet: <c>Error</c> says why, D-32).
/// <c>Error</c> is a reason code, never a raw message. <c>Runtime</c>: docker, cloudflare, kubernetes or none.
/// </summary>
public sealed record EnvironmentResource(
    string Id,
    string Name,
    string Runtime,
    string Connection,
    string? Error,
    int Workloads,
    DateTimeOffset? RefreshedAt);

/// <summary>
/// A workload. <c>Namespace</c> and <c>Node</c> are set on Kubernetes pods. <c>SupportsUsage</c> and
/// <c>SupportsLogs</c>: whether its connector serves them (Docker both, Kubernetes usage, Cloudflare neither).
/// </summary>
public sealed record WorkloadResource(
    string EnvironmentId,
    string Id,
    string Name,
    string Image,
    string State,
    string Health,
    string StatusText,
    DateTimeOffset CreatedAt,
    string? ComposeProject,
    string? Namespace,
    string? Node,
    IReadOnlyList<string> AppIds,
    StatusResource Status,
    bool Stale,
    bool SupportsUsage,
    bool SupportsLogs);

public sealed record WorkloadUsageResource(double CpuCores, long MemoryBytes, long? MemoryLimitBytes);

public sealed record LogLineResource(DateTimeOffset? Timestamp, string Stream, string Text, bool Truncated);

public static class WorkloadResourceMapper
{
    /// <param name="view">The workload.</param>
    /// <param name="source">Its environment's connector, if any.</param>
    public static WorkloadResource ToResource(this WorkloadView view, IWorkloadSource? source)
    {
        var workload = view.Workload;

        return new WorkloadResource(
            workload.EnvironmentId,
            workload.Id,
            workload.Name,
            workload.Image,
            workload.State.ToString().ToLowerInvariant(),
            workload.Health.ToString().ToLowerInvariant(),
            workload.StatusText,
            workload.CreatedAt,
            workload.ComposeProject,
            workload.Namespace,
            workload.Node,
            view.AppIds,
            StatusResource.From(view.Status),
            view.Stale,
            source?.SupportsUsage ?? false,
            source?.SupportsLogs ?? false);
    }

    public static LogLineResource ToResource(this LogLine line)
    {
        return new LogLineResource(line.Timestamp, line.Stream.ToString().ToLowerInvariant(), line.Text, line.Truncated);
    }
}
