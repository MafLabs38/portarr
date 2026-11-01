namespace Portarr.Core.Catalog;

/// <summary>
/// How much losing a data asset hurts, which sets how old its newest copy may be (the accepted loss window):
/// critical 24 h, important 7 days, comfort 30 days, unless the asset says <c>maxAge</c>.
/// </summary>
public enum DataImportance
{
    Critical,
    Important,
    Comfort,
}

/// <summary>Where a copy is kept: on the premises (the local array) or off-site (an S3 or B2 bucket).</summary>
public enum CopyTarget
{
    Local,
    Offsite,
}

public static class DataPolicy
{
    /// <summary>A copy that is exactly one window old is not late yet: runs drift by minutes, not by hours.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    public static TimeSpan DefaultMaxAge(DataImportance importance) => importance switch
    {
        DataImportance.Critical => TimeSpan.FromHours(24),
        DataImportance.Important => TimeSpan.FromDays(7),
        _ => TimeSpan.FromDays(30),
    };
}

/// <summary>
/// Data the operator follows (D-77): a database, a volume, an app's configuration… linked to an app. Only
/// what is declared is shown; nothing is discovered. <c>Kind</c> is free (postgresql, mariadb, influxdb, vault,
/// sqlite-app-config, volume, files, cluster-state…). <c>MaxAge</c> is the accepted loss window.
/// </summary>
public sealed record DataAssetDefinition(
    string Id,
    string Name,
    string AppId,
    string Kind,
    DataLocation Where,
    DataImportance Importance,
    TimeSpan MaxAge,
    IReadOnlyList<DataTargetDefinition> Targets);

/// <summary>Where the data lives, for humans: the environment (required) and whatever pins it down. Not checked against the inventory.</summary>
public sealed record DataLocation(string EnvironmentId, string? Namespace, string? Workload, string? Claim, string? Path);

/// <summary>
/// A copy the operator expects, and the sources that should prove it exists. A target without source is
/// declared but nothing produces it yet. <c>Manual</c>: the operator makes this copy himself (optionally with the
/// date of the last one); Portarr cannot read it.
/// </summary>
public sealed record DataTargetDefinition(CopyTarget Target, IReadOnlyList<BackupSourceReference> Sources, ManualCopy? Manual = null);

public sealed record ManualCopy(DateTimeOffset? LastSuccess);

public static class BackupSourceKinds
{
    public const string Backrest = "backrest";
    public const string K8up = "k8up";
    public const string CronJob = "cronjob";
}

/// <summary>
/// A job of a backup tool that produces a copy: <c>backrest@host/plan-id</c>, <c>k8up@cluster/namespace/schedule</c>,
/// <c>cronjob@cluster/namespace/name</c>. <c>Item</c> narrows a K8up schedule to what it backs up for this asset
/// (a part of the snapshot path: the claim name).
/// </summary>
public sealed record BackupSourceReference(string Kind, string EnvironmentId, string Reference, string? Item = null)
{
    public override string ToString() => $"{Kind}@{EnvironmentId}/{Reference}";
}

