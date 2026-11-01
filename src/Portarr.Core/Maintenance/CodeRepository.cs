using Portarr.Core.Catalog;

namespace Portarr.Core.Maintenance;

/// <summary>
/// Whether a security feature of a repository reports alerts: <c>On</c> (the counts are real), <c>Off</c>
/// (disabled, or never set up: no analysis), or <c>Unknown</c> (the token cannot read it; <c>Reason</c> says why).
/// </summary>
public enum ScanningState
{
    On,
    Off,
    Unknown,
}

/// <summary>
/// Open alerts by severity. Dependabot and code scanning security rules use critical, high, medium and low;
/// a code scanning rule without a security severity (a quality rule: error, warning, note) counts as <c>Other</c>.
/// </summary>
public sealed record AlertCounts(int Critical, int High, int Medium, int Low, int Other)
{
    public static AlertCounts None { get; } = new(0, 0, 0, 0, 0);

    public int Total => Critical + High + Medium + Low + Other;

    /// <summary>Critical and high: what needs attention first.</summary>
    public int Severe => Critical + High;

    public static AlertCounts From(IEnumerable<string?> severities)
    {
        int critical = 0, high = 0, medium = 0, low = 0, other = 0;
        foreach (var severity in severities)
        {
            switch (severity?.ToLowerInvariant())
            {
                case "critical":
                    critical++;
                    break;
                case "high":
                    high++;
                    break;
                case "medium" or "moderate":
                    medium++;
                    break;
                case "low":
                    low++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        return new AlertCounts(critical, high, medium, low, other);
    }
}

/// <summary>One security feature of a repository (Dependabot, code scanning, secret scanning).</summary>
public sealed record AlertScan(ScanningState State, AlertCounts Counts, string? Reason = null)
{
    public static AlertScan Off { get; } = new(ScanningState.Off, AlertCounts.None);

    public static AlertScan Unknown(string reason) => new(ScanningState.Unknown, AlertCounts.None, reason);

    public static AlertScan On(AlertCounts counts) => new(ScanningState.On, counts);
}

/// <summary>
/// The last CI run on the default branch (pushes only: a Dependabot or pull request run says nothing about the
/// branch). <c>Conclusion</c>: success, failure, cancelled, timed_out…, or null while it runs.
/// </summary>
public sealed record CiRun(string? Workflow, string Status, string? Conclusion, DateTimeOffset? At, Uri? Url)
{
    public bool Failed => Conclusion is "failure" or "timed_out" or "startup_failure";
}

/// <summary>The latest release of a repository, or its latest tag when it publishes no release.</summary>
public sealed record ReleaseInfo(string Tag, DateTimeOffset? PublishedAt, Uri? Url, bool IsTagOnly);

/// <summary>
/// A repository the operator makes (D-64), as its code host reports it. Optional facts are null when the
/// host could not tell; <c>Errors</c> lists the reads that failed (reason codes).
/// </summary>
public sealed record CodeRepository(
    string Owner,
    string Name,
    Uri Url,
    string Visibility,
    string? Language,
    string? Description,
    DateTimeOffset? PushedAt,
    string DefaultBranch,
    bool Archived,
    bool Fork,
    AlertScan Dependabot,
    AlertScan CodeScanning,
    AlertScan SecretScanning,
    ReleaseInfo? Release,
    int? DependabotPullRequests,
    CiRun? Ci,
    Uri? DependabotPullsUrl = null)
{
    public string FullName => $"{Owner}/{Name}";
}

/// <summary>What a scan of the code sources returned, or why it could not run (a reason code).</summary>
public sealed record RepositoryScan(IReadOnlyList<CodeRepository> Repositories, string? Error, DateTimeOffset? At)
{
    public static RepositoryScan NotConfigured { get; } = new([], null, null);
}

/// <summary>
/// Capability of a code host connector (D-64): GitHub first, GitLab or Gitea later. GET requests only; a
/// connector never opens a pull request or changes a setting (D-16).
/// </summary>
public interface ICodeRepositorySource
{
    /// <summary>Whether this connector reads that kind of source: the monitor hands each declared source to the one that does.</summary>
    bool Supports(ICodeSource source);

    /// <summary>A reason code when the source is declared but cannot be called (no token), else null.</summary>
    string? GetConfigurationError(ICodeSource source);

    /// <summary>
    /// The repositories of the source's owners (and its includes) that pass its filters, with their security
    /// alerts, latest release and CI state. Throws when the host cannot be read at all.
    /// </summary>
    Task<IReadOnlyList<CodeRepository>> ScanAsync(ICodeSource source, CancellationToken cancellationToken);

    /// <summary>
    /// The repository (<c>owner/name</c>) an image's OCI source label (<c>org.opencontainers.image.source</c>) points at, when it
    /// is one of this host's; null otherwise. Only the host knows what its repository addresses look like.
    /// </summary>
    string? RepositoryOfSourceLabel(string label);

    /// <summary>The repository (<c>owner/name</c>) an image is published from by the host's own naming (GHCR for GitHub), or null.</summary>
    string? RepositoryOfImage(ImageReference image);
}
