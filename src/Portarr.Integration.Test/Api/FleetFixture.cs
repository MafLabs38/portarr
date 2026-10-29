using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Api.V1.Catalog;
using Portarr.Api.V1.Workloads;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// Wiring and JSON shape of the fleet endpoints, one test per endpoint. The status rules
/// themselves are unit-tested (StatusResolverFixture, FleetServiceFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class FleetFixture : IAsyncLifetime
{
    private const string Catalog = """
        environments:
          - { id: server-1, name: Server 1, address: 10.0.0.10, docker: { url: "http://socket-proxy:2375" } }
          - { id: server-2, name: Server 2, docker: { url: "http://socket-proxy-2:2375" } }
          - { id: nas, name: NAS }
        stacks:
          - id: media
            name: Media
            members:
              - { app: jellyfin, role: critical }
              - { app: recyclarr, role: optional }
        apps:
          - id: jellyfin
            name: Jellyfin
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
          - id: recyclarr
            name: Recyclarr
            type: service
            workloads: [{ runtime: docker@server-1, match: { name: recyclarr } }]
          - id: trading
            name: Trading
            icon: trading
            workloads: [{ runtime: docker@server-2, match: { name: trading } }]
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;
    private string _dataFolder = null!;

    public FleetFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        var options = await _database.CreateOptionsAsync();
        _dataFolder = options.DataFolder;
        await File.WriteAllTextAsync(Path.Combine(_dataFolder, "catalog.yaml"), Catalog);
        Directory.CreateDirectory(Path.Combine(_dataFolder, "icons"));
        await File.WriteAllTextAsync(Path.Combine(_dataFolder, "icons", "trading.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        _factory = new PortarrFactory(options);
        _factory.Workloads.Workloads["server-1"] =
        [
            FakeWorkloadSource.Container("server-1", "jellyfin") with { Image = "lscr.io/linuxserver/jellyfin:10.10", Ports = [new PublishedPort(8096, 8096, "tcp")] },
            FakeWorkloadSource.Container("server-1", "recyclarr", WorkloadState.Exited),
            FakeWorkloadSource.Container("server-1", "watchtower"),
        ];
        _factory.Workloads.Unreachable.Add("server-2");
        _client = _factory.CreateClient();
        await _factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task should_report_the_catalog_without_its_server_path()
    {
        var status = await Get<CatalogStatusResource>("/api/v1/catalog/status");

        status.Should().Match<CatalogStatusResource>(s => s.FileName == "catalog.yaml" && s.FileExists && s.Errors.Count == 0 && s.Apps == 3);
    }

    [Fact]
    public async Task should_serve_apps_with_status_derived_lan_url_and_guessed_icon()
    {
        var jellyfin = await Get<AppResource>("/api/v1/apps/jellyfin");

        jellyfin.Status.Status.Should().Be("Healthy");
        jellyfin.Icon.Should().Be("jellyfin");
        jellyfin.Urls.Should().ContainSingle().Which.Should().Be(new AppUrlResource("LAN", "http://10.0.0.10:8096/", "lan", true));
        jellyfin.Workloads.Single().Should().Match<AppWorkloadResource>(w => w.State == "running" && w.Health == "none" && w.Role == "critical");
    }

    [Fact]
    public async Task should_serve_stacks_with_their_status()
    {
        var stack = (await Get<List<StackResource>>("/api/v1/stacks")).Single();

        stack.Status.Should().Be(new StatusResource("Degraded", "Recyclarr not healthy"));
    }

    [Fact]
    public async Task should_serve_every_discovered_container_with_the_apps_it_serves()
    {
        var workloads = await Get<List<WorkloadResource>>("/api/v1/workloads");

        workloads.Select(w => (w.Name, string.Join(",", w.AppIds))).Should().Equal(("jellyfin", "jellyfin"), ("recyclarr", "recyclarr"), ("watchtower", ""));
    }

    [Fact]
    public async Task should_report_environment_connections_with_reason_codes_only()
    {
        var environments = (await Get<List<EnvironmentResource>>("/api/v1/environments")).ToDictionary(e => e.Id);

        environments["server-1"].Should().Match<EnvironmentResource>(e => e.Connection == "connected" && e.Workloads == 3);
        environments["server-2"].Should().Match<EnvironmentResource>(e => e.Connection == "unreachable" && e.Error == "unreachable");
        environments["nas"].Should().Match<EnvironmentResource>(e => e.Connection == "not-connected" && e.Error == null);
    }

    [Fact]
    public async Task should_return_live_usage_of_a_catalog_container()
    {
        var usage = await Get<WorkloadUsageResource>("/api/v1/workloads/server-1/jellyfin/usage");

        usage.Should().Be(new WorkloadUsageResource(0.25, 100_000_000, 500_000_000));
    }

    [Fact]
    public async Task should_stream_logs_as_server_sent_events_and_signal_the_end()
    {
        using var response = await _client.GetAsync("/api/v1/workloads/server-1/jellyfin/logs?tail=10", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        body.Should().Contain("data: {\"timestamp\":\"1970-01-01T00:00:00+00:00\",\"stream\":\"stdout\",\"text\":\"jellyfin started\",\"truncated\":false}");
        body.Should().EndWith("event: end\ndata: {}\n\n");
    }

    [Theory]
    [InlineData("/api/v1/workloads/server-1/watchtower/logs")]   // not declared by any app
    [InlineData("/api/v1/workloads/server-1/watchtower/usage")]
    [InlineData("/api/v1/workloads/server-1/ghost/usage")]
    [InlineData("/api/v1/apps/ghost")]
    [InlineData("/api/v1/icons/not-in-the-catalog")]
    public async Task should_not_serve_unknown_or_undeclared_entities(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_serve_an_operator_icon_with_a_script_free_policy()
    {
        var response = await _client.GetAsync("/api/v1/icons/trading", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox").And.StartWith("default-src 'none'");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
    }

    [Fact]
    public async Task should_serve_the_new_catalog_after_a_reload()
    {
        var catalog = _factory.Services.GetRequiredService<ICatalogService>();
        await File.WriteAllTextAsync(catalog.Current.FilePath, Catalog.Replace("name: Jellyfin", "name: Jellyfin 10", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await catalog.ReloadAsync(TestContext.Current.CancellationToken);

        (await Get<AppResource>("/api/v1/apps/jellyfin")).Name.Should().Be("Jellyfin 10");
    }

    private async Task<T> Get<T>(string path)
    {
        return (await _client.GetFromJsonAsync<T>(path, TestContext.Current.CancellationToken))!;
    }
}
