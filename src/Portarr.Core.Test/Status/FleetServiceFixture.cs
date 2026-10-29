using Portarr.Core.Catalog;
using Portarr.Core.Status;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Status;

public sealed class FleetServiceFixture : CoreTest<FleetService>
{
    private static readonly CatalogEnvironment Server = new("server-1", "Server 1", new DockerEndpoint(new Uri("http://proxy:2375")), "10.0.0.10");

    [Fact]
    public void should_derive_a_lan_url_from_the_address_and_the_first_published_tcp_port()
    {
        Given(App("jellyfin", "jellyfin"), Container("jellyfin", new PublishedPort(1900, 1900, "udp"), new PublishedPort(8096, 8096, "tcp")));

        Subject.GetApps().Single().Urls.Should().ContainSingle()
            .Which.Should().Be(new AppUrlView("LAN", new Uri("http://10.0.0.10:8096"), UrlScope.Lan, true));
    }

    [Fact]
    public void should_not_derive_a_url_without_an_environment_address()
    {
        Given(App("jellyfin", "jellyfin"), Container("jellyfin", new PublishedPort(8096, 8096, "tcp")), Server with { Address = null });

        Subject.GetApps().Single().Urls.Should().BeEmpty();
    }

    [Fact]
    public void should_not_derive_a_url_for_a_service_or_when_a_lan_url_is_declared()
    {
        var service = App("db", "db") with { Type = AppType.Service };
        var declared = App("web", "web") with { Urls = [new AppUrl("LAN", new Uri("http://10.0.0.10:3000"), UrlScope.Lan)] };
        Given([service, declared], [Container("db", new PublishedPort(5432, 5432, "tcp")), Container("web", new PublishedPort(80, 8080, "tcp"))]);

        var apps = Subject.GetApps();

        apps.Single(a => a.App.Id == "db").Urls.Should().BeEmpty();
        apps.Single(a => a.App.Id == "web").Urls.Should().ContainSingle().Which.Derived.Should().BeFalse();
    }

    [Fact]
    public void should_guess_the_icon_from_the_image_unless_one_is_declared()
    {
        var guessed = App("media", "media");
        var declared = App("custom", "custom") with { Icon = "my-logo" };
        Given([guessed, declared], [Container("media") with { Image = "lscr.io/linuxserver/jellyfin:10" }, Container("custom")]);

        Subject.GetApps().Select(a => a.Icon).Should().Equal("jellyfin", "my-logo");
    }

    [Fact]
    public void should_flag_workloads_of_an_unreachable_environment_as_stale_and_unknown()
    {
        Mocker.Get<ICatalogService>().Current.Returns(State(new Portarr.Core.Catalog.Catalog([Server], [], [])));
        Mocker.Get<IWorkloadInventory>().Current.Returns(new Dictionary<string, EnvironmentInventory>
        {
            ["server-1"] = new("server-1", false, "timeout", [Container("old")], DateTimeOffset.UnixEpoch),
        });

        var workload = Subject.GetWorkloads().Single();

        workload.Stale.Should().BeTrue();
        workload.Status.Should().Be(new ResolvedStatus(HealthStatus.Unknown, "server-1 unreachable"));
    }

    private void Given(CatalogApp app, Workload workload, CatalogEnvironment? environment = null)
    {
        Given([app], [workload], environment);
    }

    private void Given(IReadOnlyList<CatalogApp> apps, IReadOnlyList<Workload> workloads, CatalogEnvironment? environment = null)
    {
        Mocker.Get<ICatalogService>().Current.Returns(State(new Portarr.Core.Catalog.Catalog([environment ?? Server], [], apps)));
        Mocker.Get<IWorkloadInventory>().Current.Returns(new Dictionary<string, EnvironmentInventory>
        {
            ["server-1"] = new("server-1", true, null, workloads, DateTimeOffset.UnixEpoch),
        });
    }

    private static CatalogState State(Portarr.Core.Catalog.Catalog catalog) => new(catalog, "/config/catalog.yaml", true, [], DateTimeOffset.UnixEpoch);

    private static CatalogApp App(string id, string container)
    {
        return new CatalogApp(id, id, AppType.Web, null, null, [], [], [new WorkloadSelector("server-1", container)]);
    }

    private static Workload Container(string name, params PublishedPort[] ports)
    {
        return new Workload("server-1", $"id-{name}", name, $"{name}:latest", WorkloadState.Running, WorkloadHealth.None, "Up 1 hour", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>(), ports);
    }
}
