using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Backrest.Test;

/// <summary>The Backrest connector (D-80): plans and their last backups, read with two POSTs of the Connect protocol.</summary>
public sealed class BackrestBackupSourceFixture
{
    private static readonly CatalogEnvironment Host = new("server-1", "Server 1", null, Backrest: new BackrestEndpoint(new Uri("http://10.0.0.10:9898/"), "BACKREST_USER", "BACKREST_PASSWORD"));

    private readonly List<(string Path, string Body, string? Authorization)> _requests = [];
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _answers = [];
    private readonly Dictionary<string, string?> _configuration = new() { ["BACKREST_USER"] = "portarr", ["BACKREST_PASSWORD"] = "s3cret" };

    public BackrestBackupSourceFixture()
    {
        // The configuration carries repository passwords and hook tokens: they must never come out.
        Ok("/v1.Backrest/GetConfig", """
            { "modno": 4, "instance": "host",
              "repos": [{ "id": "local", "uri": "/repo", "password": "never-shown", "env": ["B2_KEY=never-shown"] }],
              "plans": [
                { "id": "db-local", "repo": "local", "paths": ["/dumps"], "schedule": { "cron": "0 3 * * *" }, "hooks": [{ "actionGotify": { "token": "never-shown" } }] },
                { "id": "files", "repo": "offsite", "schedule": { "maxFrequencyDays": 1 } },
                { "id": "idle", "repo": "local", "schedule": { "disabled": true } } ] }
            """);
        Ok("/v1.Backrest/GetOperations", "db-local", """
            { "operations": [
              { "id": "10", "planId": "db-local", "status": "STATUS_SUCCESS", "unixTimeStartMs": "1769741000000", "unixTimeEndMs": "1769741060000", "snapshotId": "aaaa",
                "operationBackup": { "lastStatus": { "summary": { "totalBytesProcessed": "2048", "snapshotId": "aaaa" } } } },
              { "id": "11", "planId": "db-local", "status": "STATUS_ERROR", "unixTimeStartMs": "1769827400000", "unixTimeEndMs": "1769827460000", "operationBackup": { } },
              { "id": "12", "planId": "db-local", "status": "STATUS_INPROGRESS", "unixTimeStartMs": "1769830000000", "operationBackup": { } },
              { "id": "13", "planId": "db-local", "status": "STATUS_SUCCESS", "unixTimeStartMs": "1769831000000", "operationIndexSnapshot": { } } ] }
            """);
        Ok("/v1.Backrest/GetOperations", "files", """
            { "operations": [{ "id": "20", "planId": "files", "status": "STATUS_WARNING", "unixTimeStartMs": "1769700000000", "unixTimeEndMs": "1769700600000",
                               "operationBackup": { "lastStatus": { "summary": { "snapshotId": "bbbb" } } } }] }
            """);
        Ok("/v1.Backrest/GetOperations", "idle", "{}");
    }

    [Fact]
    public async Task should_map_plans_to_jobs_with_their_last_backup()
    {
        var jobs = await Subject().ListJobsAsync(Host, TestContext.Current.CancellationToken);

        jobs.Select(j => (j.Reference, j.Schedule)).Should().Equal(("db-local", "0 3 * * *"), ("files", "every 1 d"), ("idle", "disabled"));
        jobs[0].Repositories.Should().Equal("local");

        // A later error after a success: failed run, the success is kept. Running and non-backup operations are no attempts.
        jobs[0].Run.Should().Be(new BackupRun(At(1769827460000), BackupResult.Failed, At(1769741060000), 2048, "aaaa"));

        // A backup with warnings still made a snapshot: it counts as a success.
        jobs[1].Run.Should().Be(new BackupRun(At(1769700600000), BackupResult.Success, At(1769700600000), null, "bbbb"));
        jobs[2].Run.Should().BeNull();
    }

    [Fact]
    public async Task should_call_two_read_methods_only_with_basic_authentication_and_leak_no_secret()
    {
        var jobs = await Subject().ListJobsAsync(Host, TestContext.Current.CancellationToken);

        _requests.Select(r => r.Path).Distinct().Should().BeEquivalentTo("/v1.Backrest/GetConfig", "/v1.Backrest/GetOperations");
        _requests.Should().HaveCount(4);
        _requests.Should().AllSatisfy(r => r.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("portarr:s3cret"))));
        _requests.Select(r => r.Body).Should().Contain("""{"selector":{"planId":"db-local"},"lastN":200}""");
        System.Text.Json.JsonSerializer.Serialize(jobs.Select(j => (j.Reference, j.Name, j.Schedule, j.Repositories))).Should().NotContain("never-shown");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task should_fail_with_the_status_when_backrest_refuses(HttpStatusCode status)
    {
        _answers["/v1.Backrest/GetConfig"] = (status, "Unauthorized");

        var read = () => Subject().ListJobsAsync(Host, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task should_not_call_backrest_when_the_named_credentials_are_missing()
    {
        _configuration["BACKREST_PASSWORD"] = null;

        var read = () => Subject().ListJobsAsync(Host, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<BackupSourceUnavailableException>()).Which.Reason.Should().Be("credentials-missing");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_call_a_backrest_without_authentication_with_no_credentials()
    {
        var open = Host with { Backrest = new BackrestEndpoint(new Uri("http://10.0.0.10:9898/"), null, null) };

        await Subject().ListJobsAsync(open, TestContext.Current.CancellationToken);

        _requests.Should().AllSatisfy(r => r.Authorization.Should().BeNull());
    }

    [Theory]
    [InlineData("\"STATUS_SUCCESS\"", true)]
    [InlineData("3", true)]
    [InlineData("\"STATUS_SOMETHING_NEW\"", false)]
    public void should_read_a_status_by_name_or_number(string status, bool success)
    {
        var operation = System.Text.Json.JsonSerializer.Deserialize($$"""{ "status": {{status}} }""", BackrestJsonContext.Default.BackrestOperation);

        (operation!.Status == OperationStatus.Success).Should().Be(success);
    }

    private static DateTimeOffset At(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

    private void Ok(string path, string body) => _answers[path] = (HttpStatusCode.OK, body);

    private void Ok(string path, string plan, string body) => _answers[$"{path}#{plan}"] = (HttpStatusCode.OK, body);

    private BackrestBackupSource Subject()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(BackrestBackupSource.HttpClientName).Returns(_ => new HttpClient(new Handler(this)));
        return new BackrestBackupSource(factory, new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build());
    }

    private sealed class Handler(BackrestBackupSourceFixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (fixture._requests)
            {
                fixture._requests.Add((path, body, request.Headers.Authorization?.ToString()));
            }

            request.Method.Should().Be(HttpMethod.Post);
            var key = path;
            if (path.EndsWith("GetOperations", StringComparison.Ordinal))
            {
                var plan = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("selector").GetProperty("planId").GetString();
                key = $"{path}#{plan}";
            }

            return fixture._answers.TryGetValue(key, out var answer)
                ? new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
