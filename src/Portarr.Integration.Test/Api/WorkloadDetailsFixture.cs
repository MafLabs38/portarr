using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Workloads;
using PortarrHttp = Portarr.Http.HttpServiceCollectionExtensions;

namespace Portarr.Integration.Test.Api;

/// <summary>Guards of the logs and usage endpoints: who may read them, input bounds, connector failures, limits.</summary>
[Trait("Category", "Integration")]
public sealed class WorkloadDetailsFixture : IAsyncDisposable
{
    private const string Catalog = """
        environments:
          - { id: server-1, docker: { url: "http://socket-proxy:2375" } }
        apps:
          - id: jellyfin
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
        """;

    private readonly TestDatabase _database;
    private PortarrFactory? _factory;
    private HttpClient _client = null!;

    public WorkloadDetailsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("/api/v1/workloads/server-1/watchtower/logs")]
    [InlineData("/api/v1/workloads/server-1/watchtower/usage")]
    public async Task should_serve_details_of_an_undeclared_workload_when_logs_of_all_workloads_is_on(string path)
    {
        await StartAsync(logsOfAllWorkloads: true);

        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(100_000, 5000)]
    public async Task should_clamp_the_log_tail(int requested, int expected)
    {
        await StartAsync();

        var response = await _client.GetAsync($"/api/v1/workloads/server-1/jellyfin/logs?tail={requested}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory!.Workloads.LastTail.Should().Be(expected);
    }

    [Theory]
    [InlineData("/api/v1/workloads/server-1/jellyfin/logs")]
    [InlineData("/api/v1/workloads/server-1/jellyfin/usage")]
    public async Task should_answer_bad_gateway_when_the_connector_fails(string path)
    {
        await StartAsync();
        _factory!.Workloads.Failure = new HttpRequestException("Connection refused");

        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task should_refuse_a_log_stream_beyond_the_per_client_limit()
    {
        await StartAsync();
        _factory!.Workloads.KeepLogsOpen = true;
        var open = new List<HttpResponseMessage>();

        try
        {
            for (var i = 0; i < PortarrHttp.MaxLogStreamsPerClient; i++)
            {
                open.Add(await OpenLogsAsync());
            }

            using var refused = await OpenLogsAsync();

            open.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
            refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            open.ForEach(response => response.Dispose());
        }
    }

    private Task<HttpResponseMessage> OpenLogsAsync()
    {
        return _client.GetAsync("/api/v1/workloads/server-1/jellyfin/logs", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    private async Task StartAsync(bool logsOfAllWorkloads = false)
    {
        var options = await _database.CreateOptionsAsync();
        options.LogsOfAllWorkloads = logsOfAllWorkloads;
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog, TestContext.Current.CancellationToken);

        _factory = new PortarrFactory(options);
        _factory.Workloads.Workloads["server-1"] =
        [
            FakeWorkloadSource.Container("server-1", "jellyfin"),
            FakeWorkloadSource.Container("server-1", "watchtower"),
        ];
        _client = _factory.CreateClient();
        await _factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
    }
}
