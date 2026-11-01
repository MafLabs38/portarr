using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// Reads K8up, the restic operator for Kubernetes (D-81): <c>k8up: { schedule: namespace/name }</c>. A
/// <c>Schedule</c> (<c>k8up.io/v1</c>) says when it backs up and where; the <c>Backup</c> objects it creates say
/// whether each run succeeded; the <c>Snapshot</c> objects K8up keeps after a backup say what was saved and when,
/// per repository and path. Targets K8up v2.16 (Helm chart 4.10). Needs <c>get, list</c> on <c>schedules</c>,
/// <c>backups</c> and <c>snapshots</c> of the <c>k8up.io</c> group; the Snapshots are optional.
/// </summary>
public sealed class KubernetesK8UpBackupSource : IBackupSource
{
    private const string ScheduleKind = "Schedule";
    private const string Completed = "Completed";
    private const string PreBackupPodReady = "PreBackupPodReady";

    /// <summary>The reasons of the PreBackupPodReady condition that are not failures (K8up's status.go).</summary>
    private static readonly string[] HealthyPreBackupReasons = ["Succeeded", "Waiting", "NoPreBackupPodsFound", "Ready"];

    private readonly KubernetesApiClient _client;

    public KubernetesK8UpBackupSource(KubernetesApiClient client)
    {
        _client = client;
    }

    public string Kind => BackupSourceKinds.K8up;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Kubernetes() is not null;
    }

    public async Task<IReadOnlyList<BackupJob>> ListJobsAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var cluster = environment.Kubernetes()!;
        if (_client.GetConfigurationError(cluster) is { } error)
        {
            throw new BackupSourceUnavailableException(error);
        }

        // A 404 is a cluster without the K8up CRDs: nothing to read, not an error.
        var schedules = await _client.ListAsync(cluster, "apis/k8up.io/v1/schedules", KubernetesJsonContext.Default.ObjectListK8UpSchedule, cancellationToken) ?? [];
        if (schedules.Count == 0)
        {
            return [];
        }

        var backups = await _client.ListAsync(cluster, "apis/k8up.io/v1/backups", KubernetesJsonContext.Default.ObjectListK8UpBackup, cancellationToken) ?? [];
        var snapshots = await ReadSnapshotsAsync(cluster, cancellationToken);
        return schedules.Select(schedule => ToJob(schedule, backups, snapshots)).ToList();
    }

    private async Task<IReadOnlyList<K8UpSnapshot>> ReadSnapshotsAsync(KubernetesCluster cluster, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListAsync(cluster, "apis/k8up.io/v1/snapshots", KubernetesJsonContext.Default.ObjectListK8UpSnapshot, cancellationToken) ?? [];
        }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return [];
        }
    }

    internal static BackupJob ToJob(K8UpSchedule schedule, IReadOnlyList<K8UpBackup> backups, IReadOnlyList<K8UpSnapshot> snapshots)
    {
        var ns = schedule.Metadata.Namespace;
        var name = schedule.Metadata.Name;
        var owned = backups
            .Where(b => b.Metadata.Namespace == ns && b.Metadata.OwnerReferences?.Any(o => o.Kind == ScheduleKind && o.Name == name) == true)
            .Select(Outcome)
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .ToList();

        var success = owned.Where(o => o.Succeeded).Select(o => (DateTimeOffset?)o.At).Max();
        var failure = owned.Where(o => !o.Succeeded).Select(o => (DateTimeOffset?)o.At).Max();

        var backend = schedule.Spec?.Backup?.Backend ?? schedule.Spec?.Backend;
        var repository = RepositoryOf(backend);
        var mine = snapshots
            .Where(s => s.Metadata.Namespace == ns && repository is not null && s.Spec?.Repository == repository && s.Spec.Date is not null)
            .Select(s => new BackupSnapshot(s.Spec!.Id ?? s.Metadata.Name, s.Spec.Date!.Value, s.Spec.Paths ?? []))
            .ToList();

        // A snapshot newer than the last success the Backups show is a success too (their history may be pruned).
        var newest = mine.MaxBy(s => s.At);
        string? snapshotId = null;
        if (newest is not null && (success is null || newest.At > success))
        {
            success = newest.At;
            snapshotId = newest.Id;
        }

        var last = new[] { success, failure }.Max();
        BackupRun? run = null;
        if (last is not null)
        {
            var failed = failure is not null && (success is null || failure > success);
            run = new BackupRun(last, failed ? BackupResult.Failed : BackupResult.Success, success, null, snapshotId);
        }

        return new BackupJob($"{ns}/{name}", name, schedule.Spec?.Backup?.Schedule, [KindOf(backend)], run, mine);
    }

    /// <summary>A finished Backup: when, and whether it succeeded. A running one is not an outcome.</summary>
    private static (DateTimeOffset At, bool Succeeded)? Outcome(K8UpBackup backup)
    {
        var conditions = backup.Status?.Conditions ?? [];
        var preBackup = conditions.FirstOrDefault(c => c.Type == PreBackupPodReady);
        if (preBackup is { Status: "False" } && !HealthyPreBackupReasons.Contains(preBackup.Reason))
        {
            return (preBackup.LastTransitionTime ?? backup.Metadata.CreationTimestamp ?? DateTimeOffset.UnixEpoch, false);
        }

        var completed = conditions.FirstOrDefault(c => c.Type == Completed && c.Status == "True");
        return completed is null
            ? null
            : (completed.LastTransitionTime ?? backup.Metadata.CreationTimestamp ?? DateTimeOffset.UnixEpoch, completed.Reason == "Succeeded");
    }

    /// <summary>The repository string K8up records in its Snapshots (<c>s3:endpoint/bucket</c>, <c>b2:bucket:path</c>, a local path); null when it cannot be rebuilt (operator-wide defaults).</summary>
    internal static string? RepositoryOf(K8UpBackend? backend)
    {
        if (backend?.S3 is { Endpoint: { Length: > 0 } endpoint, Bucket: { Length: > 0 } bucket })
        {
            return $"s3:{endpoint.TrimEnd('/')}/{bucket}";
        }

        if (backend?.B2 is { Bucket: { Length: > 0 } b2Bucket })
        {
            return $"b2:{b2Bucket}:{backend.B2.Path}";
        }

        return backend?.Local?.MountPath is { Length: > 0 } path ? path : null;
    }

    /// <summary>The kind of repository, for the page: never its address or bucket.</summary>
    internal static string KindOf(K8UpBackend? backend) => backend switch
    {
        { S3: not null } => "s3",
        { B2: not null } => "b2",
        { Local: not null } => "local",
        { Gcs: not null } => "gcs",
        { Azure: not null } => "azure",
        { Swift: not null } => "swift",
        { Rest: not null } => "rest",
        _ => "default",
    };
}
