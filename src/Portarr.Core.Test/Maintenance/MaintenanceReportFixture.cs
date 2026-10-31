using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Maintenance;

/// <summary>Repositories and running images joined with the catalog, and what needs attention (D-64 to D-67).</summary>
public sealed class MaintenanceReportFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private const string Catalog = """
        sources:
          github: { owners: [example] }
        environments:
          - { id: server-1, docker: { url: "http://10.0.0.10:2375" } }
        apps:
          - { id: shop, repository: example/shop, workloads: [{ runtime: docker@server-1, match: { name: shop } }] }
          - { id: db, workloads: [{ runtime: docker@server-1, match: { name: db } }] }
        """;

    [Fact]
    public void should_link_repositories_to_apps_by_catalog_and_by_image_and_keep_the_others_as_not_deployed()
    {
        var report = Build(
            [Repository("shop"), Repository("api"), Repository("desktop-app")],
            Container("shop", "shop:1"),
            Container("api-1", "ghcr.io/example/api:2"),
            Container("tool", "tool:1", new() { [ImageCheck.SourceLabel] = "https://github.com/example/desktop-app.git" }));

        var views = report.Repositories.ToDictionary(r => r.Repository.Name);
        views["shop"].Should().Match<RepositoryView>(v => v.Deployed && v.AppIds.Single() == "shop");
        views["api"].Should().Match<RepositoryView>(v => v.Deployed && v.Images.Single() == "ghcr.io/example/api:2");
        views["desktop-app"].Deployed.Should().BeTrue("its OCI source label names it");
        report.Summary.DeployedRepositories.Should().Be(3);
    }

    [Fact]
    public void should_group_identical_running_images_and_skip_stopped_workloads()
    {
        var report = Build([], Container("proxy-1", "proxy:1"), Container("proxy-2", "proxy:1"), Container("old", "old:1") with { State = WorkloadState.Exited });

        report.Images.Should().ContainSingle().Which.Users.Select(u => u.Name).Should().Equal("proxy-1", "proxy-2");
    }

    [Fact]
    public void should_put_severe_alerts_first_and_count_what_is_off()
    {
        var vulnerable = Repository("shop") with { Dependabot = AlertScan.On(new AlertCounts(1, 2, 3, 0, 0)) };
        var failing = Repository("api") with { Ci = new CiRun("CI", "completed", "failure", Now, null) };
        var registries = new Dictionary<string, RegistryLookup>
        {
            ["docker.io/library/db"] = new([new RegistryTag("16"), new RegistryTag("18")], null, Now),
        };

        var report = Build([vulnerable, failing], registries, Container("db", "db:16"));

        report.Attention.Select(a => a.Kind).Should().StartWith(["severe-alerts", "ci-failing", "image-major"]);
        report.Attention[0].Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Danger && a.Count == 3 && a.Subjects.Single().Label == "example/shop");
        report.Attention.Single(a => a.Kind == "dependabot-off").Count.Should().Be(1);
        report.Summary.Should().Match<MaintenanceSummary>(s => s.Critical == 1 && s.High == 2 && s.Medium == 3 && s.CiFailing == 1 && s.ImagesMajor == 1);
        report.Repositories[0].Repository.Name.Should().Be("shop");
    }

    private static MaintenanceReport Build(IReadOnlyList<CodeRepository> repositories, params Workload[] workloads)
    {
        return Build(repositories, new Dictionary<string, RegistryLookup>(), workloads);
    }

    private static MaintenanceReport Build(IReadOnlyList<CodeRepository> repositories, Dictionary<string, RegistryLookup> registries, params Workload[] workloads)
    {
        var parsed = CatalogParser.Parse(Catalog);
        parsed.Errors.Should().BeEmpty();
        var inventory = new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, Now) };
        return MaintenanceService.Build(parsed.Catalog, inventory, new MaintenanceState(new RepositoryScan(repositories, null, Now), registries));
    }

    private static CodeRepository Repository(string name) => new(
        "example", name, new Uri($"https://github.com/example/{name}"), "public", "C#", null, Now, "main", false, false,
        AlertScan.Off, AlertScan.On(AlertCounts.None), AlertScan.On(AlertCounts.None), null, 0, null);

    private static Workload Container(string name, string image, Dictionary<string, string>? labels = null) =>
        new("server-1", name, name, image, WorkloadState.Running, WorkloadHealth.None, "Up", Now, null, labels ?? new Dictionary<string, string>());
}
