using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Backrest.Test;

/// <summary>Backrest passes the backup capability contract (CLAUDE.md rule 8): its plans are the jobs, its operations the runs.</summary>
public sealed class BackrestBackupSourceContractFixture : BackupSourceContract
{
    private const string Secret = "never-shown";

    protected override string ExpectedKind => BackupSourceKinds.Backrest;

    protected override IReadOnlySet<string> ReadOnlyPostPaths { get; } = new HashSet<string> { "/v1.Backrest/GetConfig", "/v1.Backrest/GetOperations" };

    protected override BackupToolHarness Arrange(ToolState state)
    {
        var requests = new List<RecordedRequest>();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(BackrestBackupSource.HttpClientName).Returns(_ => new HttpClient(new FakeBackrest(state, requests)));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BACKREST_USER"] = "portarr", ["BACKREST_PASSWORD"] = Secret }).Build();

        var environment = new CatalogEnvironment("server-1", "Server 1", null, Backrest: new BackrestEndpoint(new Uri("http://10.0.0.10:9898/"), "BACKREST_USER", "BACKREST_PASSWORD"));
        return new BackupToolHarness(
            new BackrestBackupSource(factory, configuration),
            environment,
            new CatalogEnvironment("server-2", "Server 2", null),
            () => requests.ToList(),
            [Secret, "B2_KEY"]);
    }

    private sealed class FakeBackrest(ToolState state, List<RecordedRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (requests)
            {
                requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath));
            }

            if (state.Fault == ToolFault.Unreachable)
            {
                throw new HttpRequestException("connection refused");
            }

            if (state.Fault == ToolFault.Refused)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            var path = request.RequestUri.AbsolutePath;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(path.EndsWith("GetConfig", StringComparison.Ordinal) ? Config() : Operations(PlanOf(body)));
        }

        private static string PlanOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("selector").GetProperty("planId").GetString()!;

        // The configuration carries repository passwords and hook tokens, as the real one does.
        private string Config()
        {
            var plans = string.Join(",", state.Jobs.Select(j => $$"""{ "id": "{{j.Reference}}", "repo": "local", "schedule": { "cron": "0 3 * * *" } }"""));
            return $$"""{ "repos": [{ "id": "local", "password": "{{Secret}}", "env": ["B2_KEY={{Secret}}"] }], "plans": [{{plans}}] }""";
        }

        private string Operations(string plan)
        {
            var job = state.Jobs.Single(j => j.Reference == plan);
            var success = Operation(1, "STATUS_SUCCESS", BackupSourceContract.SuccessAt);
            var failure = Operation(2, "STATUS_ERROR", BackupSourceContract.FailureAt);
            var operations = job.Behavior switch
            {
                ToolJobBehavior.Succeeded => success,
                ToolJobBehavior.FailedAfterSuccess => $"{success},{failure}",
                _ => string.Empty,
            };
            return $$"""{ "operations": [{{operations}}] }""";
        }

        private static string Operation(int id, string status, DateTimeOffset at)
        {
            var milliseconds = at.ToUnixTimeMilliseconds();
            return $$"""{ "id": "{{id}}", "status": "{{status}}", "unixTimeStartMs": "{{milliseconds}}", "unixTimeEndMs": "{{milliseconds + 60_000}}", "snapshotId": "snap{{id}}", "operationBackup": { } }""";
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
