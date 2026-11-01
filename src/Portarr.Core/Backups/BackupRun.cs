namespace Portarr.Core.Backups;

public enum BackupResult
{
    Success,
    Failed,
}

/// <summary>
/// What a backup tool says of one job: the last attempt and its result, and the last success. Any of them may
/// be unknown. <c>LastSizeBytes</c> is the size of the data the last success protected, when the tool says;
/// <c>SnapshotId</c> the snapshot it made.
/// </summary>
public sealed record BackupRun(
    DateTimeOffset? LastRunAt,
    BackupResult? LastResult,
    DateTimeOffset? LastSuccessAt,
    long? LastSizeBytes = null,
    string? SnapshotId = null);

/// <summary>A snapshot a tool kept in a repository: when, and the paths it holds (K8up <c>Snapshot</c> resources).</summary>
public sealed record BackupSnapshot(string Id, DateTimeOffset At, IReadOnlyList<string> Paths);

/// <summary>
/// A job as a backup tool defines it (a Backrest plan, a K8up Schedule, a Kubernetes CronJob).
/// <c>Reference</c> is what the catalog names (<c>plan id</c>, <c>namespace/name</c>); <c>Repositories</c> the
/// repositories it writes to, by the tool's own ids (never their addresses); <c>Snapshots</c> what the tool
/// lists independently of the job's last run, when it does.
/// <para><c>TimeZone</c> is the IANA zone the <c>Schedule</c> is read in (<c>UTC</c>, <c>Europe/Paris</c>); null when the tool does not
/// say (Backrest reads its cron in the local time of its host): the next run is then not guessed.</para>
/// </summary>
public sealed record BackupJob(
    string Reference,
    string Name,
    string? Schedule,
    IReadOnlyList<string> Repositories,
    BackupRun? Run,
    IReadOnlyList<BackupSnapshot>? Snapshots = null,
    string? TimeZone = null);
