using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>K8up passes the backup capability contract (CLAUDE.md rule 8): a Schedule is the job, its Backups the runs.</summary>
public sealed class K8upBackupSourceContractFixture : BackupSourceContract
{
    protected override string ExpectedKind => BackupSourceKinds.K8up;

    protected override string[] Expected(params string[] names) => [.. names.Select(n => $"shop/{n}")];

    protected override BackupToolHarness Arrange(ToolState state)
    {
        var cluster = new FakeCluster(path => Answer(state, path)) { Fault = state.Fault };
        return new BackupToolHarness(
            new KubernetesK8upBackupSource(cluster.Client),
            FakeCluster.Environment(),
            new CatalogEnvironment("server-1", "Server 1", null),
            () => cluster.Requests,
            [FakeCluster.Token, "never-shown-secret"]);
    }

    private static string? Answer(ToolState state, string path)
    {
        if (path.StartsWith("/apis/k8up.io/v1/schedules", StringComparison.Ordinal))
        {
            var schedules = string.Join(",", state.Jobs.Select(j => $$"""
                { "metadata": { "name": "{{j.Reference}}", "namespace": "shop" },
                  "spec": { "backend": { "s3": { "endpoint": "https://s3.example.com/", "bucket": "b", "accessKeyIDSecretRef": { "name": "never-shown-secret" } } }, "backup": { "schedule": "0 4 * * *" } } }
                """));
            return $$"""{ "items": [{{schedules}}] }""";
        }

        if (path.StartsWith("/apis/k8up.io/v1/backups", StringComparison.Ordinal))
        {
            return $$"""{ "items": [{{string.Join(",", state.Jobs.SelectMany(Backups))}}] }""";
        }

        return path.StartsWith("/apis/k8up.io/v1/snapshots", StringComparison.Ordinal) ? """{ "items": [] }""" : null;
    }

    private static IEnumerable<string> Backups(ToolJob job)
    {
        if (job.Behavior != ToolJobBehavior.NeverRan)
        {
            yield return Backup(job, 1, "Succeeded", SuccessAt);
        }

        if (job.Behavior == ToolJobBehavior.FailedAfterSuccess)
        {
            yield return Backup(job, 2, "Failed", FailureAt);
        }
    }

    private static string Backup(ToolJob job, int number, string reason, DateTimeOffset at) => $$"""
        { "metadata": { "name": "{{job.Reference}}-{{number}}", "namespace": "shop", "ownerReferences": [{ "kind": "Schedule", "name": "{{job.Reference}}" }] },
          "status": { "conditions": [{ "type": "Completed", "status": "True", "reason": "{{reason}}", "lastTransitionTime": "{{at:yyyy-MM-ddTHH:mm:ssZ}}" }] } }
        """;
}

/// <summary>Kubernetes CronJobs pass the backup capability contract (CLAUDE.md rule 8): a CronJob is the job, its last success and its Jobs the runs.</summary>
public sealed class CronJobBackupSourceContractFixture : BackupSourceContract
{
    protected override string ExpectedKind => BackupSourceKinds.CronJob;

    protected override string[] Expected(params string[] names) => [.. names.Select(n => $"shop/{n}")];

    protected override BackupToolHarness Arrange(ToolState state)
    {
        var cluster = new FakeCluster(path => Answer(state, path)) { Fault = state.Fault };
        return new BackupToolHarness(
            new KubernetesCronJobBackupSource(cluster.Client),
            FakeCluster.Environment(),
            new CatalogEnvironment("server-1", "Server 1", null),
            () => cluster.Requests,
            [FakeCluster.Token]);
    }

    private static string? Answer(ToolState state, string path)
    {
        if (path.StartsWith("/apis/batch/v1/cronjobs", StringComparison.Ordinal))
        {
            return $$"""{ "items": [{{string.Join(",", state.Jobs.Select(CronJob))}}] }""";
        }

        return path.StartsWith("/apis/batch/v1/jobs", StringComparison.Ordinal)
            ? $$"""{ "items": [{{string.Join(",", state.Jobs.Where(j => j.Behavior == ToolJobBehavior.FailedAfterSuccess).Select(FailedJob))}}] }"""
            : null;
    }

    private static string CronJob(ToolJob job)
    {
        var status = job.Behavior == ToolJobBehavior.NeverRan ? "{ }" : $$"""{ "lastSuccessfulTime": "{{SuccessAt:yyyy-MM-ddTHH:mm:ssZ}}" }""";
        return $$"""{ "metadata": { "name": "{{job.Reference}}", "namespace": "shop" }, "spec": { "schedule": "0 3 * * *" }, "status": {{status}} }""";
    }

    private static string FailedJob(ToolJob job) => $$"""
        { "metadata": { "name": "{{job.Reference}}-2", "namespace": "shop", "ownerReferences": [{ "kind": "CronJob", "name": "{{job.Reference}}" }] },
          "status": { "conditions": [{ "type": "Failed", "status": "True", "lastTransitionTime": "{{FailureAt:yyyy-MM-ddTHH:mm:ssZ}}" }] } }
        """;
}
