using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Workloads;

namespace Portarr.Core.Status;

/// <summary>
/// A status and why. <c>Reason</c> is a code the interface translates (<c>timeout</c>, <c>certificate-expires-in-days</c>) with its
/// <c>Args</c>, or, for the statuses that are not migrated yet, a sentence in English that is shown as it is.
/// </summary>
public sealed record ResolvedStatus(HealthStatus Status, string Reason, IReadOnlyList<string>? Args = null)
{
    public bool Equals(ResolvedStatus? other)
    {
        return other is not null && Status == other.Status && Reason == other.Reason && (Args ?? []).SequenceEqual(other.Args ?? []);
    }

    public override int GetHashCode() => HashCode.Combine(Status, Reason, Args?.Count ?? 0);
}

/// <summary>The codes of the reasons Core gives for what a check found (D-50, D-52, D-104); the interface says them in words.</summary>
public static class StatusReasons
{
    public const string NotChecked = "not-checked-yet";
    public const string CheckFailed = "check-failed";
    public const string HttpStatus = "http-status";
    public const string CertificateExpired = "certificate-expired";
    public const string CertificateExpiresToday = "certificate-expires-today";
    public const string CertificateExpiresInOneDay = "certificate-expires-in-one-day";
    public const string CertificateExpiresInDays = "certificate-expires-in-days";
    public const string CheckUp = "check-up";
}

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
            ({ NotConfigured: true }, _) => new ResolvedStatus(HealthStatus.Unknown, $"{selector.EnvironmentId} not connected"),
            ({ Reachable: false }, _) => new ResolvedStatus(HealthStatus.Unknown, $"{selector.EnvironmentId} unreachable"),
            (_, null) => new ResolvedStatus(HealthStatus.Down, $"{selector.Description} not found on {selector.EnvironmentId}"),
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
            return new ResolvedStatus(HealthStatus.Degraded, failingOptional.Status.Reason, failingOptional.Status.Args);
        }

        return critical.Count > 0 ? worst : resolved.MaxBy(r => r.Status.Status).Status;
    }

    /// <summary>
    /// App status from its members (V2): the worst of the workloads and the check. A non-critical check
    /// only degrades the app; a check without a result yet does not count, so a restart does not blink
    /// every app to Unknown. No member at all: Unknown.
    /// </summary>
    public static ResolvedStatus ForApp(
        IReadOnlyList<(WorkloadSelector Selector, EnvironmentInventory? Inventory, Workload? Workload)> matches,
        AppCheckView? check)
    {
        var fromCheck = StatusFromCheck(check);

        if (fromCheck is null)
        {
            // Not checked yet: an app without workloads says so rather than "not linked".
            return matches.Count == 0 && check is not null ? check.Status : ForApp(matches);
        }

        if (matches.Count == 0)
        {
            return fromCheck;
        }

        var fromWorkloads = ForApp(matches);
        return fromCheck.Status > fromWorkloads.Status ? fromCheck : fromWorkloads;
    }

    /// <summary>Days before expiry under which a certificate degrades the app (D-52).</summary>
    public static readonly TimeSpan CertificateWarning = TimeSpan.FromDays(14);

    /// <summary>
    /// Status of a check from its latest result: Down when it failed (or its certificate expired), Unknown when Portarr could not run it,
    /// Degraded when its certificate expires within <see cref="CertificateWarning"/> (D-52).
    /// </summary>
    public static ResolvedStatus ForCheck(CheckResult? latest, DateTimeOffset now)
    {
        if (latest is null)
        {
            return new(HealthStatus.Unknown, StatusReasons.NotChecked);
        }

        if (latest.Error == CheckErrors.CheckFailed)
        {
            // Portarr's own fault: the app is not known to be down, so it is not shown as down.
            return new(HealthStatus.Unknown, StatusReasons.CheckFailed);
        }

        if (latest.Error is not null)
        {
            return ForCheckError(latest.Error);
        }

        if (latest.CertificateExpiresAt is { } expiry)
        {
            if (expiry <= now)
            {
                return new(HealthStatus.Down, StatusReasons.CertificateExpired);
            }

            if (expiry - now < CertificateWarning)
            {
                var days = (int)(expiry - now).TotalDays;
                return days switch
                {
                    0 => new(HealthStatus.Degraded, StatusReasons.CertificateExpiresToday),
                    1 => new(HealthStatus.Degraded, StatusReasons.CertificateExpiresInOneDay),
                    _ => new(HealthStatus.Degraded, StatusReasons.CertificateExpiresInDays, [days.ToString(System.Globalization.CultureInfo.InvariantCulture)]),
                };
            }
        }

        return new(HealthStatus.Healthy, StatusReasons.CheckUp, [latest.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, ((int)(latest.Latency ?? TimeSpan.Zero).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    /// <summary>The status a check gives its app: none before its first result; a failing non-critical check only degrades.</summary>
    private static ResolvedStatus? StatusFromCheck(AppCheckView? check)
    {
        if (check?.State?.Latest is null)
        {
            return null;
        }

        return !check.Definition.Critical && check.Status.Status == HealthStatus.Down ? check.Status with { Status = HealthStatus.Degraded } : check.Status;
    }

    /// <summary>A check that failed: its error code is the reason (<c>timeout</c>, <c>dns-failure</c>…), an HTTP status carries its number.</summary>
    private static ResolvedStatus ForCheckError(string error)
    {
        return error switch
        {
            CheckErrors.Timeout or CheckErrors.DnsFailure or CheckErrors.ConnectionFailed or CheckErrors.TlsInvalid or CheckErrors.KeywordMissing => new(HealthStatus.Down, error),
            CheckErrors.TlsExpired => new(HealthStatus.Down, StatusReasons.CertificateExpired),
            _ when error.StartsWith("http-", StringComparison.Ordinal) => new(HealthStatus.Down, StatusReasons.HttpStatus, [error[5..]]),
            _ => new(HealthStatus.Down, CheckErrors.InvalidResponse),
        };
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
