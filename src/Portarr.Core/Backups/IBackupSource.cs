using Portarr.Core.Catalog;

namespace Portarr.Core.Backups;

/// <summary>
/// Capability of a connector that reads the jobs of a backup tool (D-62): Backrest plans, K8up schedules, the
/// CronJobs of a Kubernetes cluster. Read-only: Portarr reports what the tool did, it never starts, configures
/// or restores a backup (D-43).
/// </summary>
public interface IBackupSource
{
    /// <summary>The kind a catalog source names (<see cref="BackupSourceKinds"/>).</summary>
    string Kind { get; }

    bool Supports(CatalogEnvironment environment);

    /// <summary>
    /// Every job the tool defines on the environment, with its last runs. Throws when the tool cannot be read
    /// (unreachable, refused, not configured): the sources that name it are then shown unknown, and why.
    /// </summary>
    Task<IReadOnlyList<BackupJob>> ListJobsAsync(CatalogEnvironment environment, CancellationToken cancellationToken);
}

/// <summary>Thrown by a source whose environment is declared but cannot be called yet; <c>Reason</c> is a reason code.</summary>
public sealed class BackupSourceUnavailableException : Exception
{
    public BackupSourceUnavailableException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public BackupSourceUnavailableException()
        : this("unavailable")
    {
    }

    public BackupSourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = message;
    }

    public string Reason { get; }
}
