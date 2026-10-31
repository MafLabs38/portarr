using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;

namespace Portarr.Api.V1.Maintenance;

/// <summary>A security feature of a repository. <c>State</c>: on, off or unknown (<c>Reason</c>: a reason code).</summary>
public sealed record AlertScanResource(string State, int Critical, int High, int Medium, int Low, int Other, int Total, string? Reason);

public sealed record ReleaseResource(string Tag, DateTimeOffset? PublishedAt, Uri? Url, bool TagOnly);

/// <summary>The last push-triggered run of the default branch. <c>Conclusion</c> is null while it runs.</summary>
public sealed record CiRunResource(string? Workflow, string Status, string? Conclusion, bool Failed, DateTimeOffset? At, Uri? Url);

/// <summary>A repository the operator makes (D-64). <c>Deployed</c>: an app of the catalog or a running image is built from it.</summary>
public sealed record RepositoryResource(
    string FullName,
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
    AlertScanResource Dependabot,
    AlertScanResource CodeScanning,
    AlertScanResource SecretScanning,
    ReleaseResource? Release,
    int? DependabotPullRequests,
    CiRunResource? Ci,
    IReadOnlyList<string> AppIds,
    IReadOnlyList<string> Images,
    bool Deployed);

public sealed record ImageUserResource(string EnvironmentId, string EnvironmentName, string Name, string? Namespace, IReadOnlyList<string> AppIds);

/// <summary>
/// A running image (D-66). <c>Status</c>: up-to-date, behind, newer-build, floating, unknown or pending;
/// <c>Change</c>: build, patch, minor or major; <c>Reason</c>: a reason code (image-id, floating, pinned, not-found,
/// refused, rate-limited…). <c>FromLabel</c>: the current version is the image's OCI version label.
/// </summary>
public sealed record DeployedImageResource(
    string Reference,
    string Registry,
    string Repository,
    string? Tag,
    string Status,
    string? Current,
    string? Latest,
    string? Change,
    DateTimeOffset? CurrentAt,
    DateTimeOffset? LatestAt,
    string? Reason,
    bool FromLabel,
    IReadOnlyList<ImageUserResource> Users,
    string? RepositoryFullName);

public sealed record AttentionSubjectResource(string Label, Uri? Url, int? Count);

/// <summary>A finding of the summary. <c>Level</c>: danger, warning or info.</summary>
public sealed record AttentionItemResource(string Kind, string Level, int Count, IReadOnlyList<AttentionSubjectResource> Subjects);

/// <summary>The code source: declared in the catalog, when it was last scanned and why it failed (a reason code).</summary>
public sealed record CodeSourceResource(bool Configured, IReadOnlyList<string> Owners, DateTimeOffset? ScannedAt, string? Error);

public sealed record MaintenanceSummaryResource(
    int Repositories,
    int DeployedRepositories,
    int Critical,
    int High,
    int Medium,
    int Low,
    int QualityAlerts,
    int SecretAlerts,
    int DependabotOff,
    int CodeScanningOff,
    int CiFailing,
    int DependabotPullRequests,
    int Images,
    int ImagesUpToDate,
    int ImagesBehind,
    int ImagesMajor,
    int ImagesFloating,
    int ImagesUnknown);

/// <summary>
/// When the data was read and when it is read next. <c>GitHubNextAt</c> is null while no code source is scanned;
/// <c>Refreshing</c>: a manual refresh was accepted and its pass has not finished; <c>AvailableAt</c>: when the next
/// manual refresh is accepted (null: now).
/// </summary>
public sealed record MaintenanceRefreshResource(
    DateTimeOffset? GitHubAt,
    DateTimeOffset? GitHubNextAt,
    DateTimeOffset? RegistriesAt,
    bool Refreshing,
    DateTimeOffset? AvailableAt);

public sealed record MaintenanceResource(
    MaintenanceSummaryResource Summary,
    IReadOnlyList<AttentionItemResource> Attention,
    CodeSourceResource CodeSource,
    IReadOnlyList<RepositoryResource> Repositories,
    IReadOnlyList<DeployedImageResource> Images,
    MaintenanceRefreshResource Refresh);

public static class MaintenanceResourceMapper
{
    public static MaintenanceResource ToResource(this MaintenanceReport report, Core.Catalog.Catalog catalog)
    {
        var environments = catalog.Environments.ToDictionary(e => e.Id, e => e.Name, StringComparer.Ordinal);
        var s = report.Summary;
        return new MaintenanceResource(
            new MaintenanceSummaryResource(
                s.Repositories, s.DeployedRepositories, s.Critical, s.High, s.Medium, s.Low, s.QualityAlerts, s.SecretAlerts, s.DependabotOff,
                s.CodeScanningOff, s.CiFailing, s.DependabotPullRequests, s.Images, s.ImagesUpToDate, s.ImagesBehind, s.ImagesMajor, s.ImagesFloating, s.ImagesUnknown),
            report.Attention.Select(a => new AttentionItemResource(
                a.Kind,
                a.Level.ToString().ToLowerInvariant(),
                a.Count,
                a.Subjects.Select(subject => new AttentionSubjectResource(subject.Label, subject.Url, subject.Count)).ToList())).ToList(),
            new CodeSourceResource(report.CodeSourceConfigured, catalog.GitHub?.Owners ?? [], report.Scan.At, report.Scan.Error),
            report.Repositories.Select(ToResource).ToList(),
            report.Images.Select(image => ToResource(image, environments)).ToList(),
            ToResource(report.Refresh));
    }

    public static MaintenanceRefreshResource ToResource(this MaintenanceRefresh refresh)
    {
        return new MaintenanceRefreshResource(refresh.GitHubAt, refresh.GitHubNextAt, refresh.RegistriesAt, refresh.Refreshing, refresh.AvailableAt);
    }

    private static RepositoryResource ToResource(RepositoryView view)
    {
        var r = view.Repository;
        return new RepositoryResource(
            r.FullName,
            r.Owner,
            r.Name,
            r.Url,
            r.Visibility,
            r.Language,
            r.Description,
            r.PushedAt,
            r.DefaultBranch,
            r.Archived,
            r.Fork,
            ToResource(r.Dependabot),
            ToResource(r.CodeScanning),
            ToResource(r.SecretScanning),
            r.Release is null ? null : new ReleaseResource(r.Release.Tag, r.Release.PublishedAt, r.Release.Url, r.Release.IsTagOnly),
            r.DependabotPullRequests,
            r.Ci is null ? null : new CiRunResource(r.Ci.Workflow, r.Ci.Status, r.Ci.Conclusion, r.Ci.Failed, r.Ci.At, r.Ci.Url),
            view.AppIds,
            view.Images,
            view.Deployed);
    }

    private static AlertScanResource ToResource(AlertScan scan)
    {
        var c = scan.Counts;
        return new AlertScanResource(scan.State.ToString().ToLowerInvariant(), c.Critical, c.High, c.Medium, c.Low, c.Other, c.Total, scan.Reason);
    }

    private static DeployedImageResource ToResource(DeployedImageView view, Dictionary<string, string> environments)
    {
        var check = view.Check;
        return new DeployedImageResource(
            view.Image.Display,
            view.Image.Registry,
            view.Image.Repository,
            view.Image.Tag,
            check.Status switch
            {
                ImageStatus.UpToDate => "up-to-date",
                ImageStatus.NewerBuild => "newer-build",
                _ => check.Status.ToString().ToLowerInvariant(),
            },
            check.Current,
            check.Latest,
            check.Change?.ToString().ToLowerInvariant(),
            check.CurrentAt,
            check.LatestAt,
            check.Reason,
            check.FromLabel,
            view.Users.Select(u => new ImageUserResource(u.EnvironmentId, environments.GetValueOrDefault(u.EnvironmentId) ?? u.EnvironmentId, u.Name, u.Namespace, u.AppIds)).ToList(),
            view.RepositoryFullName);
    }
}
