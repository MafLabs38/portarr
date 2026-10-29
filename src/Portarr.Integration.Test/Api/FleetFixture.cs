using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Api.V1.Catalog;
using Portarr.Api.V1.Workloads;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class FleetFixture : IAsyncLifetime
{
    private const string Catalog = """
        environments:
          - { id: server-1, name: Server 1, address: 10.0.0.10, docker: { url: "http://socket-proxy:2375" } }
          - { id: server-2, name: Server 2, docker: { url: "http://socket-proxy-2:2375" } }
        stacks:
          - id: media
            name: Media
            members:
              - { app: sonarr, role: critical }
              - { app: recyclarr, role: optional }
        apps:
          - id: sonarr
            name: Sonarr
            icon: sonarr
            urls: [{ label: Public, href: "https://sonarr.homelab.example" }]
            workloads: [{ runtime: docker@server-1, match: { name: sonarr } }]
          - id: recyclarr
            name: Recyclarr
            type: service
            workloads: [{ runtime: docker@server-1, match: { name: recyclarr } }]
          - id: docs
            name: Docs
            urls: [{ href: "https://docs.homelab.example" }]
          - id: grafana
            name: Grafana
            workloads: [{ runtime: docker@server-2, match: { name: grafana } }]
          - id: jellyfin
            name: Jellyfin
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;

    public FleetFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);

        _factory = new PortarrFactory(options);
        _factory.Workloads.Workloads["server-1"] =
        [
            FakeWorkloadSource.Container("server-1", "sonarr", health: WorkloadHealth.Healthy),
            FakeWorkloadSource.Container("server-1", "recyclarr", WorkloadState.Exited),
            FakeWorkloadSource.Container("server-1", "watchtower"),
            FakeWorkloadSource.Container("server-1", "jellyfin") with { Image = "lscr.io/linuxserver/jellyfin:10.10", Ports = [new PublishedPort(8096, 8096, "tcp")] },
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
    public async Task should_load_the_catalog_file()
    {
        var status = await Get<CatalogStatusResource>("/api/v1/catalog/status");

        status.FileExists.Should().BeTrue();
        status.Errors.Should().BeEmpty();
        (status.Apps, status.Stacks, status.Environments).Should().Be((5, 1, 2));
    }

    [Fact]
    public async Task should_resolve_each_app_status_from_its_container()
    {
        var apps = (await Get<List<AppResource>>("/api/v1/apps")).ToDictionary(a => a.Id);

        apps["sonarr"].Status.Status.Should().Be("Healthy");
        apps["sonarr"].Stack.Should().Be(new AppStackResource("media", "Media", "critical"));
        apps["sonarr"].Urls.Should().ContainSingle().Which.Href.Should().Be("https://sonarr.homelab.example/");
        apps["recyclarr"].Status.Status.Should().Be("Down");
        apps["docs"].Status.Should().Be(new StatusResource("Unknown", "Not linked to a workload"));
        apps["grafana"].Status.Reason.Should().Be("server-2 unreachable");
    }

    [Fact]
    public async Task should_derive_a_lan_url_and_an_icon_from_the_container()
    {
        var jellyfin = await Get<AppResource>("/api/v1/apps/jellyfin");

        jellyfin.Urls.Should().ContainSingle().Which.Should().Be(new AppUrlResource("LAN", "http://10.0.0.10:8096/", "lan", true));
        jellyfin.Icon.Should().Be("jellyfin");
        jellyfin.Workloads.Single().Should().Match<AppWorkloadResource>(w => w.State == "running" && w.Health == "none" && w.Role == "critical");
    }

    [Fact]
    public async Task should_keep_declared_public_urls_and_not_derive_one_without_a_lan_address()
    {
        var sonarr = await Get<AppResource>("/api/v1/apps/sonarr");

        sonarr.Urls.Should().ContainSingle().Which.Scope.Should().Be("public");
    }

    [Fact]
    public async Task should_degrade_the_stack_when_an_optional_member_is_down()
    {
        var stack = (await Get<List<StackResource>>("/api/v1/stacks")).Single();

        stack.Status.Should().Be(new StatusResource("Degraded", "Recyclarr not healthy"));
    }

    [Fact]
    public async Task should_list_every_discovered_container_and_the_apps_it_serves()
    {
        var workloads = await Get<List<WorkloadResource>>("/api/v1/workloads");

        workloads.Select(w => w.Name).Should().Equal("jellyfin", "recyclarr", "sonarr", "watchtower");
        workloads.Single(w => w.Name == "sonarr").AppIds.Should().Equal("sonarr");
        workloads.Single(w => w.Name == "watchtower").AppIds.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_unreachable_environments()
    {
        var environments = (await Get<List<EnvironmentResource>>("/api/v1/environments")).ToDictionary(e => e.Id);

        environments["server-1"].Should().Match<EnvironmentResource>(e => e.Reachable && e.Workloads == 4);
        environments["server-2"].Should().Match<EnvironmentResource>(e => !e.Reachable && e.Error == "Connection refused");
    }

    [Fact]
    public async Task should_return_live_usage_of_a_container()
    {
        var usage = await Get<WorkloadUsageResource>("/api/v1/workloads/server-1/sonarr/usage");

        usage.Should().Be(new WorkloadUsageResource(0.25, 100_000_000, 500_000_000));
    }

    [Fact]
    public async Task should_stream_logs_as_server_sent_events()
    {
        using var response = await _client.GetAsync("/api/v1/workloads/server-1/sonarr/logs?tail=10", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        body.Should().Contain("data: {\"timestamp\":\"1970-01-01T00:00:00+00:00\",\"stream\":\"stdout\",\"text\":\"sonarr started\"}");
        body.Should().Contain("\"stream\":\"stderr\"");
    }

    [Theory]
    [InlineData("/api/v1/workloads/server-1/ghost/usage")]
    [InlineData("/api/v1/workloads/nowhere/sonarr/logs")]
    [InlineData("/api/v1/apps/ghost")]
    public async Task should_return_not_found_for_unknown_entities(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_reload_the_catalog_when_the_file_changes()
    {
        var path = _factory.Services.GetRequiredService<ICatalogService>().Current.FilePath;
        await File.WriteAllTextAsync(path, Catalog.Replace("name: Sonarr", "name: Sonarr 4", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        await _factory.Services.GetRequiredService<ICatalogService>().ReloadAsync(TestContext.Current.CancellationToken);

        var sonarr = await Get<AppResource>("/api/v1/apps/sonarr");

        sonarr.Name.Should().Be("Sonarr 4");
    }

    private async Task<T> Get<T>(string path)
    {
        return (await _client.GetFromJsonAsync<T>(path, TestContext.Current.CancellationToken))!;
    }
}
