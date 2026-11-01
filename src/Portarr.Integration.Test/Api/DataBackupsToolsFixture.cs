using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Backups;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// V3 over the real HTTP pipeline: the declared data compared with what Backrest (Connect API) and the Kubernetes
/// API (CronJobs, K8up) report, played by WireMock. The states themselves are unit-tested (DataServiceFixture) and
/// each connector has its contract suite; this proves the wiring (catalog, connectors, monitor, API, roles).
/// </summary>
[Trait("Category", "Integration")]
public sealed class DataBackupsToolsFixture : IAsyncLifetime
{
    private readonly TestDatabase _database;
    private readonly ToolServer _backrest = new();
    private readonly ToolServer _cluster = new();
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;

    public DataBackupsToolsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        // 2026-01-01T12:00:00Z is the factory clock.
        _backrest
            .ServePost("/v1.Backrest/GetConfig", "{}", """
                { "repos": [{ "id": "local", "password": "never-shown" }, { "id": "b2", "password": "never-shown" }],
                  "plans": [
                    { "id": "db-local", "repo": "local", "schedule": { "cron": "0 3 * * *" } },
                    { "id": "db-offsite", "repo": "b2", "schedule": { "cron": "30 4 * * *" } },
                    { "id": "files-local", "repo": "local", "schedule": { "cron": "0 2 * * *" } } ] }
                """)
            .ServePost("/v1.Backrest/GetOperations", "db-local", """
                { "operations": [{ "id": "1", "status": "STATUS_SUCCESS", "unixTimeStartMs": "1767236400000", "unixTimeEndMs": "1767236460000", "snapshotId": "aaaa", "operationBackup": { } }] }
                """)
            .ServePost("/v1.Backrest/GetOperations", "db-offsite", "{}")
            .ServePost("/v1.Backrest/GetOperations", "files-local", """
                { "operations": [
                  { "id": "2", "status": "STATUS_SUCCESS", "unixTimeStartMs": "1767150000000", "unixTimeEndMs": "1767150060000", "operationBackup": { } },
                  { "id": "3", "status": "STATUS_ERROR", "unixTimeStartMs": "1767236400000", "unixTimeEndMs": "1767236460000", "operationBackup": { } }] }
                """);
        _cluster
            .Serve("GET", "/apis/batch/v1/namespaces/shop/cronjobs", """{ "items": [{ "metadata": { "name": "dump", "namespace": "shop" }, "spec": { "schedule": "0 1 * * *" }, "status": { "lastSuccessfulTime": "2026-01-01T01:02:00Z" } }] }""")
            .Serve("GET", "/apis/batch/v1/namespaces/shop/jobs", """{ "items": [] }""");

        var catalog = $$"""
            environments:
              - { id: server-1, name: Server 1, docker: { url: "http://socket-proxy:2375" }, backrest: { url: "{{_backrest.Url}}", allowInsecure: true, userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }
              - { id: server-2, name: Server 2, docker: { url: "http://socket-proxy-2:2375" } }
              - { id: cluster, name: Cluster, kubernetes: { server: "{{_cluster.Url}}", allowInsecure: true, tokenEnv: CLUSTER_TOKEN } }
            apps:
              - { id: shop, name: Shop }
              - { id: media, name: Media }
            data:
              - id: shop-db
                app: shop
                name: Shop database
                kind: postgresql
                where: { environment: cluster, namespace: shop }
                importance: critical
                targets:
                  local: { cronjob: shop/dump }
                  offsite: { backrest: { plan: db-offsite, environment: server-1 } }
              - id: media-files
                app: media
                name: Media files
                kind: files
                where: { environment: server-1 }
                importance: important
                targets:
                  local: { backrest: { plan: files-local } }
              - id: media-config
                app: media
                name: Media config
                kind: sqlite-app-config
                where: { environment: server-2 }
                importance: important
                targets:
                  local: { backrest: { plan: config } }
            """;
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), catalog);
        _factory = new PortarrFactory(options, new Dictionary<string, string?>
        {
            ["BACKREST_USER"] = "portarr",
            ["BACKREST_PASSWORD"] = "s3cret",
            ["CLUSTER_TOKEN"] = "cluster-token",
        });
        _client = await _factory.CreateSignedInClientAsync();
        await _factory.Services.GetRequiredService<IBackupMonitor>().RefreshAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _backrest.Dispose();
        _cluster.Dispose();
    }

    [Fact]
    public async Task should_tell_a_proven_copy_a_failed_one_a_job_waiting_and_a_tool_not_set_up_apart()
    {
        var data = await GetDataAsync();
        var assets = data.GetProperty("assets").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);

        // CronJob that succeeded within the window (local) and a Backrest plan that never ran (off-site): waiting.
        assets["shop-db"].GetProperty("local").GetProperty("state").GetString().Should().Be("protected");
        var offsite = assets["shop-db"].GetProperty("offsite");
        offsite.GetProperty("state").GetString().Should().Be("waiting");
        offsite.GetProperty("reason").GetString().Should().Be("no-run");
        offsite.GetProperty("nextRunAt").ValueKind.Should().Be(JsonValueKind.Null, "Backrest reads its cron in its host's local time, which Portarr cannot see: only the cron text is shown");
        assets["shop-db"].GetProperty("state").GetString().Should().Be("waiting");

        // The last Backrest attempt failed after a success the day before.
        assets["media-files"].GetProperty("state").GetString().Should().Be("failed");
        assets["media-files"].GetProperty("local").GetProperty("lastResult").GetString().Should().Be("failed");

        // Backrest is not configured on server-2.
        assets["media-config"].GetProperty("state").GetString().Should().Be("not-set-up");
        assets["media-config"].GetProperty("local").GetProperty("reason").GetString().Should().Be("no-connector");
    }

    [Fact]
    public async Task should_count_waiting_and_not_set_up_apart_from_what_is_at_risk()
    {
        var summary = (await GetDataAsync()).GetProperty("summary");

        summary.GetProperty("followed").GetInt32().Should().Be(3);
        summary.GetProperty("waiting").GetInt32().Should().Be(1);
        summary.GetProperty("notSetUp").GetInt32().Should().Be(1);
        summary.GetProperty("failed").GetInt32().Should().Be(1);
        summary.GetProperty("atRisk").GetInt32().Should().Be(1, "only the failed one is a proven problem");
        summary.GetProperty("criticalAssets").GetInt32().Should().Be(1);
        summary.GetProperty("criticalOffsite").GetInt32().Should().Be(0);
        summary.GetProperty("criticalOffsitePending").GetInt32().Should().Be(1, "the critical off-site job exists and has not run yet");
    }

    [Fact]
    public async Task should_read_the_tools_with_the_credentials_from_the_environment_and_never_write()
    {
        await GetDataAsync();

        _backrest.Calls.Should().OnlyContain(c => c == "POST /v1.Backrest/GetConfig" || c == "POST /v1.Backrest/GetOperations");
        _backrest.Authorizations.Should().OnlyContain(a => a == "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("portarr:s3cret")));
        _cluster.Calls.Should().OnlyContain(c => c.StartsWith("GET /apis/batch/v1/namespaces/shop/", StringComparison.Ordinal), "only the namespace the catalog names is read, never the whole cluster");
        _cluster.Authorizations.Should().OnlyContain(a => a == "Bearer cluster-token");
    }

    [Fact]
    public async Task should_list_the_tools_read_and_why_one_could_not_be()
    {
        var sources = (await GetDataAsync()).GetProperty("sources").EnumerateArray().ToList();

        sources.Select(s => (s.GetProperty("tool").GetString(), s.GetProperty("environmentId").GetString(), s.GetProperty("error").ValueKind))
            .Should().BeEquivalentTo(
            [
                ("backrest", "server-1", JsonValueKind.Null),
                ("backrest", "server-2", JsonValueKind.String),
                ("cronjob", "cluster", JsonValueKind.Null),
            ]);
        sources.Single(s => s.GetProperty("environmentId").GetString() == "server-2").GetProperty("error").GetString().Should().Be("no-connector");
    }

    [Fact]
    public async Task should_never_return_what_the_tools_answer_with_beyond_jobs_and_runs()
    {
        var text = (await _client.GetStringAsync("/api/v1/data", TestContext.Current.CancellationToken));

        text.Should().NotContain("never-shown").And.NotContain("s3cret").And.NotContain("cluster-token");
    }

    private async Task<JsonElement> GetDataAsync()
    {
        return await _client.GetFromJsonAsync<JsonElement>("/api/v1/data", TestContext.Current.CancellationToken);
    }
}
