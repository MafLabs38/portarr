using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>The Kubernetes backup sources (D-62, D-81): CronJobs with their Jobs, K8up Schedules with their Backups and Snapshots.</summary>
public sealed class KubernetesBackupSourceFixture : IDisposable
{
    private static readonly CatalogEnvironment Environment = new("cluster", "Cluster", null, Kubernetes: new KubernetesCluster(new Uri("https://10.0.0.10:6443"), "CLUSTER_TOKEN", null));

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = [];
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-k8s-backup-").FullName;

    [Fact]
    public async Task should_read_cronjobs_and_the_attempts_of_their_jobs()
    {
        Ok("/apis/batch/v1/cronjobs?limit=500", """
            { "items": [
              { "metadata": { "name": "dump", "namespace": "shop" }, "spec": { "schedule": "0 3 * * *" }, "status": { "lastSuccessfulTime": "2026-01-30T03:02:00Z" } },
              { "metadata": { "name": "quiet", "namespace": "shop" }, "status": { } } ] }
            """);
        Ok("/apis/batch/v1/jobs?limit=500", """
            { "items": [
              { "metadata": { "name": "dump-1", "namespace": "shop", "ownerReferences": [{ "kind": "CronJob", "name": "dump" }] },
                "status": { "conditions": [{ "type": "Failed", "status": "True", "lastTransitionTime": "2026-01-31T03:05:00Z" }] } },
              { "metadata": { "name": "other-1", "namespace": "other", "ownerReferences": [{ "kind": "CronJob", "name": "dump" }] },
                "status": { "conditions": [{ "type": "Complete", "status": "True", "lastTransitionTime": "2026-01-31T04:00:00Z" }] } } ] }
            """);

        var jobs = await Cron().ListJobsAsync(Environment, TestContext.Current.CancellationToken);

        var dump = jobs.Single(j => j.Reference == "shop/dump");
        dump.Schedule.Should().Be("0 3 * * *");
        dump.Run.Should().Be(new BackupRun(At("2026-01-31T03:05:00Z"), BackupResult.Failed, At("2026-01-30T03:02:00Z")), "a failed attempt after the last success is a failed run; a job of another namespace is not its own");
        jobs.Single(j => j.Reference == "shop/quiet").Run.Should().BeNull();
        _requests.Should().AllSatisfy(r => r.Method.Should().Be(HttpMethod.Get));
    }

    [Fact]
    public async Task should_still_read_the_cronjobs_when_the_jobs_are_refused()
    {
        Ok("/apis/batch/v1/cronjobs?limit=500", """{ "items": [{ "metadata": { "name": "dump", "namespace": "shop" }, "status": { "lastSuccessfulTime": "2026-01-30T03:02:00Z" } }] }""");
        _responses["/apis/batch/v1/jobs?limit=500"] = (HttpStatusCode.Forbidden, "{}");

        var jobs = await Cron().ListJobsAsync(Environment, TestContext.Current.CancellationToken);

        jobs.Single().Run.Should().Be(new BackupRun(At("2026-01-30T03:02:00Z"), BackupResult.Success, At("2026-01-30T03:02:00Z")));
    }

    [Fact]
    public async Task should_say_http_403_while_the_read_only_account_lacks_the_rule()
    {
        _responses["/apis/batch/v1/cronjobs?limit=500"] = (HttpStatusCode.Forbidden, "{}");

        var read = () => Cron().ListJobsAsync(Environment, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_name_a_missing_token_instead_of_calling_the_cluster()
    {
        var read = () => new KubernetesCronJobBackupSource(Client(token: null)).ListJobsAsync(Environment, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<BackupSourceUnavailableException>()).Which.Reason.Should().Be("token-missing");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_k8up_schedules_with_their_backups_and_the_snapshots_of_their_repository()
    {
        Ok("/apis/k8up.io/v1/schedules?limit=500", """
            { "items": [
              { "metadata": { "name": "offsite", "namespace": "shop" },
                "spec": { "backend": { "s3": { "endpoint": "https://s3.example.com/", "bucket": "backups" } }, "backup": { "schedule": "0 4 * * *" } } },
              { "metadata": { "name": "local", "namespace": "shop" },
                "spec": { "backup": { "schedule": "@daily-random", "backend": { "local": { "mountPath": "/data/repo" } } } } } ] }
            """);
        Ok("/apis/k8up.io/v1/backups?limit=500", """
            { "items": [
              { "metadata": { "name": "offsite-backup-1", "namespace": "shop", "ownerReferences": [{ "kind": "Schedule", "name": "offsite" }] },
                "status": { "conditions": [{ "type": "Completed", "status": "True", "reason": "Succeeded", "lastTransitionTime": "2026-01-30T04:10:00Z" }] } },
              { "metadata": { "name": "offsite-backup-2", "namespace": "shop", "ownerReferences": [{ "kind": "Schedule", "name": "offsite" }] },
                "status": { "conditions": [{ "type": "Completed", "status": "True", "reason": "Failed", "lastTransitionTime": "2026-01-31T04:10:00Z" }] } },
              { "metadata": { "name": "offsite-backup-3", "namespace": "shop", "ownerReferences": [{ "kind": "Schedule", "name": "offsite" }] },
                "status": { "started": true, "conditions": [{ "type": "Progressing", "status": "True", "reason": "Started" }] } },
              { "metadata": { "name": "local-backup-1", "namespace": "shop", "ownerReferences": [{ "kind": "Schedule", "name": "local" }] },
                "status": { "conditions": [{ "type": "PreBackupPodReady", "status": "False", "reason": "Failed", "lastTransitionTime": "2026-01-31T05:00:00Z" }] } } ] }
            """);
        Ok("/apis/k8up.io/v1/snapshots?limit=500", """
            { "items": [
              { "metadata": { "name": "abcd1234", "namespace": "shop" }, "spec": { "id": "abcd1234", "date": "2026-01-30T04:10:05Z", "paths": ["/data/data-db-0"], "repository": "s3:https://s3.example.com/backups" } },
              { "metadata": { "name": "ffff0000", "namespace": "shop" }, "spec": { "id": "ffff0000", "date": "2026-01-31T09:00:00Z", "paths": ["/data/data-db-0"], "repository": "s3:https://elsewhere.example.com/other" } } ] }
            """);

        var jobs = await K8up().ListJobsAsync(Environment, TestContext.Current.CancellationToken);

        var offsite = jobs.Single(j => j.Reference == "shop/offsite");
        offsite.Schedule.Should().Be("0 4 * * *");
        offsite.Repositories.Should().Equal("s3");
        offsite.Run.Should().Be(new BackupRun(At("2026-01-31T04:10:00Z"), BackupResult.Failed, At("2026-01-30T04:10:05Z"), null, "abcd1234"), "the snapshot of its own repository is the newest success; a running backup is no outcome");
        offsite.Snapshots.Should().ContainSingle().Which.Paths.Should().Equal("/data/data-db-0");
        jobs.Single(j => j.Reference == "shop/local").Run.Should().Be(new BackupRun(At("2026-01-31T05:00:00Z"), BackupResult.Failed, null), "a pre-backup pod that failed fails the backup");
    }

    [Fact]
    public async Task should_find_no_job_in_a_cluster_without_the_k8up_crds_and_keep_working_without_the_snapshots()
    {
        (await K8up().ListJobsAsync(Environment, TestContext.Current.CancellationToken)).Should().BeEmpty();

        Ok("/apis/k8up.io/v1/schedules?limit=500", """{ "items": [{ "metadata": { "name": "s", "namespace": "shop" } }] }""");
        Ok("/apis/k8up.io/v1/backups?limit=500", """{ "items": [] }""");
        _responses["/apis/k8up.io/v1/snapshots?limit=500"] = (HttpStatusCode.Forbidden, "{}");

        (await K8up().ListJobsAsync(Environment, TestContext.Current.CancellationToken)).Should().ContainSingle().Which.Run.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "s3": { "endpoint": "https://s3.example.com/", "bucket": "b" } }""", "s3:https://s3.example.com/b")]
    [InlineData("""{ "b2": { "bucket": "b", "path": "p" } }""", "b2:b:p")]
    [InlineData("""{ "local": { "mountPath": "/repo" } }""", "/repo")]
    [InlineData("""{ "s3": { } }""", null)]
    public void should_rebuild_the_repository_string_k8up_records_in_its_snapshots(string backend, string? expected)
    {
        var parsed = System.Text.Json.JsonSerializer.Deserialize<K8UpBackend>(backend, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        KubernetesK8UpBackupSource.RepositoryOf(parsed).Should().Be(expected);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private void Ok(string path, string body) => _responses[path] = (HttpStatusCode.OK, body);

    private KubernetesCronJobBackupSource Cron() => new(Client());

    private KubernetesK8UpBackupSource K8up() => new(Client());

    private KubernetesApiClient Client(string? token = "test-token")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CLUSTER_TOKEN"] = token }).Build();
        var options = Options.Create(new PortarrOptions { CatalogFile = Path.Combine(_folder, "catalog.yaml") });
        return new KubernetesApiClient(configuration, options, (_, _) => new Handler(this));
    }

    private sealed class Handler(KubernetesBackupSourceFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture._requests.Add(request);
            return Task.FromResult(fixture._responses.TryGetValue(request.RequestUri!.PathAndQuery, out var answer)
                ? new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
