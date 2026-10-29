using Portarr.Api.V1.Catalog;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Api.V1.Workloads;

/// <summary>
/// An environment. <c>Connection</c>: connected, unreachable, pending (not polled yet) or
/// not-connected (no connector declared, D-32). <c>Error</c> is a reason code, never a raw message.
/// </summary>
public sealed record EnvironmentResource(
    string Id,
    string Name,
    string Runtime,
    string Connection,
    string? Error,
    int Workloads,
    DateTimeOffset? RefreshedAt);

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
    IReadOnlyList<string> AppIds,
    StatusResource Status,
    bool Stale);

public sealed record WorkloadUsageResource(double CpuCores, long MemoryBytes, long? MemoryLimitBytes);

public sealed record LogLineResource(DateTimeOffset? Timestamp, string Stream, string Text, bool Truncated);

public static class WorkloadResourceMapper
{
    public static WorkloadResource ToResource(this WorkloadView view)
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
            view.AppIds,
            StatusResource.From(view.Status),
            view.Stale);
    }

    public static LogLineResource ToResource(this LogLine line)
    {
        return new LogLineResource(line.Timestamp, line.Stream.ToString().ToLowerInvariant(), line.Text, line.Truncated);
    }
}
