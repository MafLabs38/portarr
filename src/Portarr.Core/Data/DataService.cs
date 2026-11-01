using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Core.Data;

/// <summary>
/// How one expected copy stands, from best to worst: <c>Protected</c> (a success within the accepted loss window),
/// <c>Waiting</c> (the job exists and has not run yet: pending, neither a failure nor a risk), <c>Unknown</c> (the
/// tool cannot be read: unreachable, refused, unexpected answer), <c>NotSetUp</c> (the tool is declared but not
/// configured on that environment: the operator must fix it), <c>Late</c> (the newest success is older than the
/// window), <c>Failed</c> (the last attempt failed after the last success), <c>Missing</c> (the copy is declared
/// and no job produces it). Waiting and NotSetUp used to read "Unknown" (D-79, amended).
/// </summary>
public enum CopyState
{
    Protected,
    Waiting,
    Unknown,
    NotSetUp,
    Late,
    Failed,
    Missing,
}

/// <summary>
/// What one source says of one expected copy. <c>Reason</c> is a code: a connector failure (<c>http-403</c>,
/// <c>no-connector</c>), <c>not-found</c> (the tool has no such job), <c>no-run</c>, <c>not-in-snapshots</c>.
/// <c>NextRunAt</c>: when a job that never ran fires next, if its schedule is a cron expression.
/// </summary>
public sealed record SourceCheck(
    BackupSourceReference Reference,
    CopyState State,
    string? Reason,
    string? JobName,
    string? Schedule,
    IReadOnlyList<string> Repositories,
    BackupRun? Run,
    DateTimeOffset? NextRunAt = null);

/// <summary>
/// One expected copy of one asset (its local or its off-site target): the best state of its sources, the newest
/// success among them, and the sources themselves. <c>NextRunAt</c>: for a copy waiting for its first run, the
/// soonest scheduled run among its sources.
/// </summary>
public sealed record CopyView(
    CopyTarget Target,
    CopyState State,
    string? Reason,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastRunAt,
    BackupResult? LastResult,
    long? SizeBytes,
    ManualCopy? Manual,
    IReadOnlyList<SourceCheck> Sources,
    DateTimeOffset? NextRunAt = null);

/// <summary>A followed asset with its two copies; <c>State</c> is the worst of the copies declared.</summary>
public sealed record DataAssetView(DataAssetDefinition Asset, CopyState State, CopyView? Local, CopyView? Offsite);

/// <summary>A tool read for the page: how fresh the reading is, and why it failed.</summary>
public sealed record DataSourceView(string Kind, string EnvironmentId, string? Error, int Jobs, DateTimeOffset CheckedAt);

/// <summary>
/// The page's figures. <c>AtRisk</c>: late, failed or missing (waiting, unknown and not set up are not counted:
/// nothing proves a loss). <c>CriticalAssets</c> and <c>CriticalOffsite</c>: "x of y critical assets have an
/// off-site copy" (a copy that succeeded at least once); <c>CriticalOffsitePending</c> of the others have an
/// off-site job that has not run yet, so they are not protected but not missing either.
/// </summary>
public sealed record DataSummary(
    int Followed,
    int Protected,
    int Late,
    int Failed,
    int Missing,
    int Unknown,
    int AtRisk,
    int CriticalAssets,
    int CriticalOffsite,
    int Waiting = 0,
    int NotSetUp = 0,
    int CriticalOffsitePending = 0);

public sealed record DataReport(DataSummary Summary, IReadOnlyList<DataAssetView> Assets, IReadOnlyList<DataSourceView> Sources);

public interface IDataService
{
    DataReport GetReport();
}

/// <summary>
/// Compares the declared policy (the followed data, their importance and expected copies) with what the backup
/// tools report (D-77, D-78). The only rule of its own is the accepted loss window: a copy is late once its newest
/// success is older than the asset's <c>maxAge</c> (plus a small grace).
/// </summary>
public sealed class DataService : IDataService
{
    public const string NoRun = "no-run";

    /// <summary>The reason of a source the monitor has not read yet.</summary>
    public const string NotReadYet = "not-read-yet";

    /// <summary>How far ahead a date the operator wrote by hand may be (clocks differ); beyond it, it is a typo.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);

    private readonly ICatalogService _catalogService;
    private readonly IBackupMonitor _backups;
    private readonly TimeProvider _timeProvider;

    public DataService(ICatalogService catalogService, IBackupMonitor backups, TimeProvider timeProvider)
    {
        _catalogService = catalogService;
        _backups = backups;
        _timeProvider = timeProvider;
    }

    public DataReport GetReport()
    {
        return Build(_catalogService.Current.Catalog, _backups.Current, _timeProvider.GetUtcNow());
    }

    public static DataReport Build(Catalog.Catalog catalog, BackupMonitorState backups, DateTimeOffset now)
    {
        var apps = catalog.Apps.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);
        var views = catalog.DataAssets
            .Select(asset => ViewOf(asset, backups, now))
            .OrderByDescending(v => v.State)
            .ThenBy(v => v.Asset.Importance)
            .ThenBy(v => apps.GetValueOrDefault(v.Asset.AppId) ?? v.Asset.AppId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var critical = views.Where(v => v.Asset.Importance == DataImportance.Critical).ToList();
        var summary = new DataSummary(
            views.Count,
            views.Count(v => v.State == CopyState.Protected),
            views.Count(v => v.State == CopyState.Late),
            views.Count(v => v.State == CopyState.Failed),
            views.Count(v => v.State == CopyState.Missing),
            views.Count(v => v.State == CopyState.Unknown),
            views.Count(v => v.State is CopyState.Late or CopyState.Failed or CopyState.Missing),
            critical.Count,
            critical.Count(v => v.Offsite?.LastSuccessAt is not null),
            views.Count(v => v.State == CopyState.Waiting),
            views.Count(v => v.State == CopyState.NotSetUp),
            critical.Count(v => v.Offsite is { LastSuccessAt: null, State: CopyState.Waiting }));

        var sources = backups.Readings.Values
            .OrderBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.EnvironmentId, StringComparer.Ordinal)
            .Select(r => new DataSourceView(r.Kind, r.EnvironmentId, r.Error, r.Jobs.Count, r.CheckedAt))
            .ToList();

        return new DataReport(summary, views, sources);
    }

    /// <summary>
    /// One source's verdict on one expected copy. A tool that is not configured on the environment is NotSetUp
    /// (the operator must set it up); one that cannot be read is Unknown (with its reason); a tool that answers
    /// without the job is Missing; a job that never ran is Waiting, with its next run when the schedule tells. A K8up schedule narrowed to an item (<c>Item</c>) is judged
    /// by the newest snapshot whose paths hold it, when the tool lists snapshots.
    /// </summary>
    public static SourceCheck Check(BackupSourceReference reference, SourceReading? reading, TimeSpan maxAge, DateTimeOffset now)
    {
        // Not read yet (the start, a catalog reload): not a tool that is not set up.
        if (reading is null)
        {
            return Unjudged(reference, CopyState.Unknown, NotReadYet);
        }

        if (reading.Error is { } error)
        {
            return Unjudged(reference, reading.NotSetUp ? CopyState.NotSetUp : CopyState.Unknown, error);
        }

        var job = reading.Jobs.FirstOrDefault(j => j.Reference == reference.Reference);
        if (job is null)
        {
            return Unjudged(reference, CopyState.Missing, "not-found");
        }

        var run = job.Run;
        if (reference.Item is { } item && job.Snapshots is { Count: > 0 } snapshots)
        {
            var matching = snapshots.Where(s => s.Paths.Any(path => HoldsItem(path, item))).MaxBy(s => s.At);
            if (matching is null)
            {
                return new SourceCheck(reference, CopyState.Missing, "not-in-snapshots", job.Name, job.Schedule, job.Repositories, job.Run);
            }

            run = new BackupRun(run?.LastRunAt, run?.LastResult, matching.At, run?.LastSizeBytes, matching.Id);
        }

        var (state, reason) = Judge(run, maxAge, now);
        var next = state == CopyState.Waiting ? NextRun(job, now) : null;
        return new SourceCheck(reference, state, reason, job.Name, job.Schedule, job.Repositories, run, next);
    }

    /// <summary>
    /// The state of a copy known only by its last success: protected within the window, late beyond; a job that
    /// never succeeded nor failed is waiting for its first run.
    /// </summary>
    public static (CopyState State, string? Reason) Judge(BackupRun? run, TimeSpan maxAge, DateTimeOffset now)
    {
        if (run is null)
        {
            return (CopyState.Waiting, NoRun);
        }

        // The latest attempt failed, after the last success.
        if (run.LastResult == BackupResult.Failed && (run.LastSuccessAt is null || run.LastRunAt is null || run.LastRunAt >= run.LastSuccessAt))
        {
            return (CopyState.Failed, null);
        }

        if (run.LastSuccessAt is not { } success)
        {
            return (CopyState.Waiting, NoRun);
        }

        return now - success > maxAge + DataPolicy.Grace ? (CopyState.Late, null) : (CopyState.Protected, null);
    }

    /// <summary>Whether a snapshot path is the item or lies under it, by whole path segments: item <c>db</c> is not <c>/data/db-old</c>.</summary>
    private static bool HoldsItem(string path, string item) => $"/{path.Trim('/')}/".Contains($"/{item.Trim('/')}/", StringComparison.Ordinal);

    /// <summary>The next firing, in the zone the tool reads its schedule in; none when the tool does not say (the cron text is all there is).</summary>
    private static DateTimeOffset? NextRun(BackupJob job, DateTimeOffset now)
    {
        if (job.TimeZone is not { } name)
        {
            return null;
        }

        try
        {
            return CronSchedule.NextAfter(job.Schedule, now, TimeZoneInfo.FindSystemTimeZoneById(name));
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static (CopyState State, string? Reason) JudgeManual(ManualCopy manual, TimeSpan maxAge, DateTimeOffset now)
    {
        if (manual.LastSuccess is not { } at)
        {
            return (CopyState.Unknown, "manual");
        }

        // A date in the future is a typo, not a backup that has not happened yet: it would read as just made.
        return at > now + FutureTolerance ? (CopyState.Unknown, "manual-in-the-future") : Judge(new BackupRun(at, BackupResult.Success, at), maxAge, now);
    }

    private static SourceCheck Unjudged(BackupSourceReference reference, CopyState state, string reason)
    {
        return new SourceCheck(reference, state, reason, null, null, [], null);
    }

    private static DataAssetView ViewOf(DataAssetDefinition asset, BackupMonitorState backups, DateTimeOffset now)
    {
        var local = asset.Targets.FirstOrDefault(t => t.Target == CopyTarget.Local) is { } l ? CopyOf(asset, l, backups, now) : null;
        var offsite = asset.Targets.FirstOrDefault(t => t.Target == CopyTarget.Offsite) is { } o ? CopyOf(asset, o, backups, now) : null;
        var state = new[] { local?.State, offsite?.State }.Max() ?? CopyState.Missing;
        return new DataAssetView(asset, state, local, offsite);
    }

    private static CopyView CopyOf(DataAssetDefinition asset, DataTargetDefinition target, BackupMonitorState backups, DateTimeOffset now)
    {
        var checks = target.Sources
            .Select(source => Check(source, backups.Readings.GetValueOrDefault(SourceReading.KeyOf(source.Kind, source.EnvironmentId)), asset.MaxAge, now))
            .ToList();

        // A copy the operator makes himself counts like a source that reports it: only with a date can it be judged.
        (CopyState State, string? Reason)? manual = target.Manual is null ? null : JudgeManual(target.Manual, asset.MaxAge, now);

        if (checks.Count == 0 && manual is null)
        {
            return new CopyView(target.Target, CopyState.Missing, "no-source", null, null, null, null, null, checks);
        }

        var best = checks.OrderBy(c => c.State).ThenByDescending(c => c.Run?.LastSuccessAt).FirstOrDefault();
        var state = best?.State ?? manual!.Value.State;
        var reason = best?.Reason ?? manual?.Reason;
        if (manual is { } m && m.State < state)
        {
            state = m.State;
            reason = m.Reason;
        }

        var newest = checks.Select(c => c.Run?.LastSuccessAt).Append(target.Manual?.LastSuccess).Where(d => d is not null).Max();
        return new CopyView(
            target.Target,
            state,
            reason,
            newest,
            best?.Run?.LastRunAt,
            best?.Run?.LastResult,
            newest is null ? null : checks.Select(c => c.Run).FirstOrDefault(r => r?.LastSuccessAt == newest)?.LastSizeBytes,
            target.Manual,
            checks,
            state == CopyState.Waiting ? checks.Where(c => c.State == CopyState.Waiting).Min(c => c.NextRunAt) : null);
    }
}
