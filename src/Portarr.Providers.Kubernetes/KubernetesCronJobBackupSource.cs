using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// Reads the backup jobs a cluster runs as Kubernetes CronJobs (D-62): <c>cronjob: namespace/name</c>. A CronJob
/// says when its last job succeeded; the Jobs it started say which attempts failed, so the last run is the newest
/// finished Job. The Jobs are optional (a cluster that refuses them still gives the CronJob's last success).
/// Needs <c>get, list</c> on <c>cronjobs</c> and <c>jobs</c> of the <c>batch</c> group; until then the source
/// answers <c>http-403</c>.
/// </summary>
public sealed class KubernetesCronJobBackupSource : IBackupSource
{
    private readonly KubernetesApiClient _client;

    public KubernetesCronJobBackupSource(KubernetesApiClient client)
    {
        _client = client;
    }

    public string Kind => BackupSourceKinds.CronJob;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Kubernetes is not null;
    }

    public async Task<IReadOnlyList<BackupJob>> ListJobsAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var cluster = environment.Kubernetes!;
        if (_client.GetConfigurationError(cluster) is { } error)
        {
            throw new BackupSourceUnavailableException(error);
        }

        var cronJobs = await _client.ListAsync(cluster, "apis/batch/v1/cronjobs", KubernetesJsonContext.Default.ObjectListCronJob, cancellationToken) ?? [];
        var jobs = await ReadJobsAsync(cluster, cancellationToken);
        return cronJobs.Select(cronJob => ToJob(cronJob, jobs)).ToList();
    }

    /// <summary>Optional: without the Jobs, a failure after the last success cannot be seen.</summary>
    private async Task<IReadOnlyList<BatchJob>> ReadJobsAsync(KubernetesCluster cluster, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListAsync(cluster, "apis/batch/v1/jobs", KubernetesJsonContext.Default.ObjectListBatchJob, cancellationToken) ?? [];
        }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return [];
        }
    }

    internal static BackupJob ToJob(CronJob cronJob, IReadOnlyList<BatchJob> jobs)
    {
        var owned = jobs
            .Where(job => job.Metadata.Namespace == cronJob.Metadata.Namespace && job.Metadata.OwnerReferences?.Any(o => o.Kind == "CronJob" && o.Name == cronJob.Metadata.Name) == true)
            .ToList();

        DateTimeOffset? Finished(BatchJob job, string type) => job.Status?.Conditions?.FirstOrDefault(c => c.Type == type && c.Status == "True") is { } condition
            ? job.Status.CompletionTime ?? condition.LastTransitionTime
            : null;

        var success = owned.Select(job => Finished(job, "Complete")).Append(cronJob.Status?.LastSuccessfulTime).Max();
        var failure = owned.Select(job => Finished(job, "Failed")).Max();
        var last = new[] { success, failure }.Max();

        BackupRun? run = null;
        if (last is not null)
        {
            var failed = failure is not null && (success is null || failure > success);
            run = new BackupRun(last, failed ? BackupResult.Failed : BackupResult.Success, success);
        }

        var name = cronJob.Metadata.Name;
        return new BackupJob($"{cronJob.Metadata.Namespace}/{name}", name, cronJob.Spec?.Schedule, [], run);
    }
}
