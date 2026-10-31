using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Core.Data;

namespace Portarr.Api.V1.Data;

/// <summary>Where the data lives, as the catalog declares it (everything but the environment is optional).</summary>
public sealed record DataWhereResource(string EnvironmentId, string EnvironmentName, string? Namespace, string? Workload, string? Claim, string? Path);

/// <summary>
/// What one source says of an expected copy. <c>Tool</c>: backrest, k8up or cronjob. <c>State</c>: protected,
/// waiting (the job never ran), unknown (the tool cannot be read), not-set-up (the tool is not configured), late,
/// failed or missing. <c>Reason</c>: a code (<c>http-403</c>, <c>no-connector</c>, <c>not-found</c>…).
/// <c>Repositories</c>: the tool's own repository ids or kinds, never addresses. <c>NextRunAt</c>: when a job
/// that never ran fires next (a cron schedule read in UTC), if known.
/// </summary>
public sealed record DataSourceCheckResource(
    string Tool,
    string EnvironmentId,
    string Reference,
    string? Item,
    string State,
    string? Reason,
    string? JobName,
    string? Schedule,
    IReadOnlyList<string> Repositories,
    DateTimeOffset? LastRunAt,
    string? LastResult,
    DateTimeOffset? LastSuccessAt,
    string? SnapshotId,
    DateTimeOffset? NextRunAt);

/// <summary>
/// One expected copy of an asset (<c>local</c> or <c>offsite</c>). <c>Manual</c>: the operator makes it himself;
/// <c>ManualLastSuccess</c> is the date he wrote down. <c>NextRunAt</c>: for a copy waiting for its first run,
/// the soonest scheduled run, if known.
/// </summary>
public sealed record DataCopyResource(
    string State,
    string? Reason,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastRunAt,
    string? LastResult,
    long? SizeBytes,
    bool Manual,
    DateTimeOffset? ManualLastSuccess,
    IReadOnlyList<DataSourceCheckResource> Sources,
    DateTimeOffset? NextRunAt);

/// <summary>
/// A followed data asset (D-77) with the state of its copies. <c>Importance</c>: critical, important or comfort.
/// <c>MaxAgeSeconds</c>: the accepted loss window. <c>State</c>: the worst of the copies declared. A copy that is
/// not declared is null.
/// </summary>
public sealed record DataAssetResource(
    string Id,
    string Name,
    string AppId,
    string Kind,
    DataWhereResource Where,
    string Importance,
    long MaxAgeSeconds,
    string State,
    DataCopyResource? Local,
    DataCopyResource? Offsite);

/// <summary>A tool the declared data expects, with the freshness of its last reading and why it failed, if it did.</summary>
public sealed record DataSourceResource(string Tool, string EnvironmentId, string EnvironmentName, string? Error, int Jobs, DateTimeOffset CheckedAt);

/// <summary>
/// The page's figures (see <see cref="DataSummary"/>). <c>Waiting</c>: assets whose job never ran, <c>NotSetUp</c>:
/// assets whose tool is not configured; neither is counted in <c>AtRisk</c>. <c>CriticalOffsitePending</c>:
/// critical assets whose off-site job has not run yet (not in <c>CriticalOffsite</c>, not missing either).
/// </summary>
public sealed record DataSummaryResource(
    int Followed,
    int Protected,
    int Late,
    int Failed,
    int Missing,
    int Unknown,
    int AtRisk,
    int CriticalAssets,
    int CriticalOffsite,
    int Waiting,
    int NotSetUp,
    int CriticalOffsitePending);

public sealed record DataResource(DataSummaryResource Summary, IReadOnlyList<DataAssetResource> Assets, IReadOnlyList<DataSourceResource> Sources);

public static class DataResourceMapper
{
    public static DataResource ToResource(this DataReport report, IReadOnlyList<CatalogEnvironment> environments)
    {
        var names = environments.ToDictionary(e => e.Id, e => e.Name, StringComparer.Ordinal);
        var s = report.Summary;
        return new DataResource(
            new DataSummaryResource(s.Followed, s.Protected, s.Late, s.Failed, s.Missing, s.Unknown, s.AtRisk, s.CriticalAssets, s.CriticalOffsite, s.Waiting, s.NotSetUp, s.CriticalOffsitePending),
            report.Assets.Select(view => ToResource(view, names)).ToList(),
            report.Sources.Select(source => new DataSourceResource(source.Kind, source.EnvironmentId, names.GetValueOrDefault(source.EnvironmentId) ?? source.EnvironmentId, source.Error, source.Jobs, source.CheckedAt)).ToList());
    }

    /// <summary>The state as the API spells it: <c>protected</c>, <c>waiting</c>, <c>unknown</c>, <c>not-set-up</c>, <c>late</c>, <c>failed</c>, <c>missing</c>.</summary>
    public static string ToResource(this CopyState state) => state == CopyState.NotSetUp ? "not-set-up" : state.ToString().ToLowerInvariant();

    public static string ToResource(this DataImportance importance) => importance.ToString().ToLowerInvariant();

    private static DataAssetResource ToResource(DataAssetView view, Dictionary<string, string> names)
    {
        var asset = view.Asset;
        var where = asset.Where;
        return new DataAssetResource(
            asset.Id,
            asset.Name,
            asset.AppId,
            asset.Kind,
            new DataWhereResource(where.EnvironmentId, names.GetValueOrDefault(where.EnvironmentId) ?? where.EnvironmentId, where.Namespace, where.Workload, where.Claim, where.Path),
            asset.Importance.ToResource(),
            (long)asset.MaxAge.TotalSeconds,
            view.State.ToResource(),
            view.Local is null ? null : ToResource(view.Local),
            view.Offsite is null ? null : ToResource(view.Offsite));
    }

    private static DataCopyResource ToResource(CopyView copy)
    {
        return new DataCopyResource(
            copy.State.ToResource(),
            copy.Reason,
            copy.LastSuccessAt,
            copy.LastRunAt,
            copy.LastResult?.ToString().ToLowerInvariant(),
            copy.SizeBytes,
            copy.Manual is not null,
            copy.Manual?.LastSuccess,
            copy.Sources.Select(ToResource).ToList(),
            copy.NextRunAt);
    }

    private static DataSourceCheckResource ToResource(SourceCheck check)
    {
        return new DataSourceCheckResource(
            check.Reference.Kind,
            check.Reference.EnvironmentId,
            check.Reference.Reference,
            check.Reference.Item,
            check.State.ToResource(),
            check.Reason,
            check.JobName,
            check.Schedule,
            check.Repositories,
            check.Run?.LastRunAt,
            check.Run?.LastResult?.ToString().ToLowerInvariant(),
            check.Run?.LastSuccessAt,
            check.Run?.SnapshotId,
            check.NextRunAt);
    }
}
