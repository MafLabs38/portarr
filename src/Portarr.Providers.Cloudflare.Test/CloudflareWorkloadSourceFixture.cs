using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Cloudflare.Test;

public sealed class CloudflareWorkloadSourceFixture
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";

    private static readonly CatalogEnvironment Environment = new("cdn", "CDN", null, null, new CloudflareAccount(AccountId, "CF_TOKEN"));

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, string> _responses = [];
    private readonly Dictionary<string, string?> _configuration = new() { ["CF_TOKEN"] = "test-token" };

    [Fact]
    public async Task should_list_pages_projects_across_pages_and_workers_with_a_bearer_token_and_get_only()
    {
        _responses[$"/client/v4/accounts/{AccountId}/pages/projects?page=1"] = """
            { "success": true, "result_info": { "page": 1, "total_pages": 2 },
              "result": [{ "id": "1", "name": "site", "canonical_deployment": { "id": "d", "environment": "production", "latest_stage": { "name": "deploy", "status": "success" } } }] }
            """;
        _responses[$"/client/v4/accounts/{AccountId}/pages/projects?page=2"] = """
            { "success": true, "result_info": { "page": 2, "total_pages": 2 }, "result": [{ "id": "2", "name": "docs" }] }
            """;
        _responses[$"/client/v4/accounts/{AccountId}/workers/scripts"] = """
            { "success": true, "result": [{ "id": "api", "modified_on": "2026-10-20T14:03:00Z" }] }
            """;

        var workloads = await Source().ListAsync(Environment, TestContext.Current.CancellationToken);

        workloads.Select(w => w.Name).Should().Equal("site", "docs", "api");
        _requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Get);
            r.Headers.Authorization!.ToString().Should().Be("Bearer test-token");
        });
    }

    [Fact]
    public async Task should_fail_on_an_unsuccessful_answer()
    {
        _responses[$"/client/v4/accounts/{AccountId}/pages/projects?page=1"] = """{ "success": false, "result": null }""";

        await FluentActions.Awaiting(() => Source().ListAsync(Environment, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void should_report_a_missing_token_or_a_placeholder_account_as_not_configured()
    {
        Source().GetConfigurationError(Environment).Should().BeNull();
        Source().GetConfigurationError(Environment with { Cloudflare = new CloudflareAccount("TODO", "CF_TOKEN") }).Should().Be("account-invalid");

        _configuration.Clear();
        Source().GetConfigurationError(Environment).Should().Be("token-missing");
    }

    [Fact]
    public async Task should_serve_neither_usage_nor_logs()
    {
        var source = Source();

        source.SupportsUsage.Should().BeFalse();
        source.SupportsLogs.Should().BeFalse();
        (await source.GetUsageAsync(Environment, "site", TestContext.Current.CancellationToken)).Should().BeNull();
        (await source.OpenLogsAsync(Environment, "site", 10, false, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    private CloudflareWorkloadSource Source()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(CloudflareWorkloadSource.HttpClientName)
            .Returns(_ => new HttpClient(new Handler(this)) { BaseAddress = CloudflareWorkloadSource.ApiBaseAddress });

        return new CloudflareWorkloadSource(factory, new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build());
    }

    private sealed class Handler(CloudflareWorkloadSourceFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture._requests.Add(request);
            return Task.FromResult(fixture._responses.TryGetValue(request.RequestUri!.PathAndQuery, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
