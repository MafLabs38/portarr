using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Status;

public sealed record ResolvedStatus(HealthStatus Status, string Reason);

/// <summary>
/// Combines the states reported by the connectors (D-30: orchestration only, no metric
/// evaluation): workload → app → stack, following spec §5.
/// </summary>
public static class StatusResolver
{
    public static ResolvedStatus ForWorkload(Workload workload)
    {
        return (workload.State, workload.Health) switch
        {
            (WorkloadState.Running, WorkloadHealth.Unhealthy) => new(HealthStatus.Down, "Healthcheck failing"),
            (WorkloadState.Running, WorkloadHealth.Starting) => new(HealthStatus.Unknown, "Healthcheck starting"),
            (WorkloadState.Running, WorkloadHealth.Healthy) => new(HealthStatus.Healthy, "Running, healthcheck passing"),
            (WorkloadState.Running, _) => new(HealthStatus.Healthy, "Running"),
            (WorkloadState.Restarting, _) => new(HealthStatus.Down, "Restarting"),
            (WorkloadState.Paused, _) => new(HealthStatus.Paused, "Paused"),
            (WorkloadState.Exited or WorkloadState.Dead, _) => new(HealthStatus.Down, workload.StatusText),
            _ => new(HealthStatus.Unknown, workload.StatusText),
        };
    }

    /// <summary>Status of one workload selector: its environment, then the matched container.</summary>
    public static ResolvedStatus ForSelector(WorkloadSelector selector, EnvironmentInventory? inventory, Workload? workload)
    {
        return (inventory, workload) switch
        {
            (null, _) => new ResolvedStatus(HealthStatus.Unknown, $"{selector.EnvironmentId} not inventoried yet"),
            ({ Reachable: false }, _) => new ResolvedStatus(HealthStatus.Unknown, $"{selector.EnvironmentId} unreachable"),
            (_, null) => new ResolvedStatus(HealthStatus.Down, $"Container {selector.Name} not found on {selector.EnvironmentId}"),
            _ => ForWorkload(workload),
        };
    }

    /// <summary>
    /// App status from its workloads: critical ones decide, a failing optional one only degrades
    /// the app (D-48). No workload at all: Unknown.
    /// </summary>
    public static ResolvedStatus ForApp(IReadOnlyList<(WorkloadSelector Selector, EnvironmentInventory? Inventory, Workload? Workload)> matches)
    {
        if (matches.Count == 0)
        {
            return new(HealthStatus.Unknown, "Not linked to a workload");
        }

        var resolved = matches.Select(m => (m.Selector.Role, Status: ForSelector(m.Selector, m.Inventory, m.Workload))).ToList();

        var critical = resolved.Where(r => r.Role == MemberRole.Critical).Select(r => r.Status).ToList();
        var worst = critical.Count > 0 ? critical.MaxBy(s => s.Status)! : new ResolvedStatus(HealthStatus.Healthy, "Running");
        if (worst.Status >= HealthStatus.Degraded)
        {
            return worst;
        }

        var failingOptional = resolved.FirstOrDefault(r => r.Role == MemberRole.Optional && r.Status.Status >= HealthStatus.Degraded);
        if (failingOptional != default)
        {
            return new ResolvedStatus(HealthStatus.Degraded, failingOptional.Status.Reason);
        }

        return critical.Count > 0 ? worst : resolved.MaxBy(r => r.Status.Status).Status;
    }

    public static ResolvedStatus ForStack(IReadOnlyList<(StackMember Member, string AppName, HealthStatus Status)> members)
    {
        if (members.Count == 0)
        {
            return new(HealthStatus.Unknown, "No members");
        }

        var downCritical = members.FirstOrDefault(m => m.Member.Role == MemberRole.Critical && m.Status == HealthStatus.Down);
        if (downCritical != default)
        {
            return new(HealthStatus.Down, $"{downCritical.AppName} is down");
        }

        var notHealthy = members.Where(m => m.Status != HealthStatus.Healthy).ToList();
        if (notHealthy.Count == 0)
        {
            return new(HealthStatus.Healthy, "All members healthy");
        }

        // Only unknown members: we cannot claim it is degraded.
        if (members.All(m => m.Status == HealthStatus.Unknown))
        {
            return new(HealthStatus.Unknown, "No data yet");
        }

        return new(HealthStatus.Degraded, $"{string.Join(", ", notHealthy.Select(m => m.AppName))} not healthy");
    }
}
