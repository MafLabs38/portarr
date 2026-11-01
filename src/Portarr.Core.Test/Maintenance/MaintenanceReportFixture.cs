using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

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

    private static AttentionItem Item(MaintenanceReport report, string kind) => report.Attention.Single(a => a.Kind == kind);

    [Fact]
    public void should_raise_secret_alerts_as_danger_counting_the_alerts()
    {
        var leaky = Repository("shop") with { SecretScanning = AlertScan.On(new AlertCounts(0, 0, 0, 0, 3)) };

        var item = Item(Build([leaky]), "secret-alerts");

        item.Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Danger && a.Count == 3 && a.Subjects.Single().Label == "example/shop" && a.Subjects.Single().Count == 3);
    }

    [Fact]
    public void should_raise_the_lesser_alerts_as_information_apart_from_the_severe_ones()
    {
        var mixed = Repository("shop") with { Dependabot = AlertScan.On(new AlertCounts(1, 0, 2, 1, 0)) };

        var report = Build([mixed]);

        Item(report, "severe-alerts").Count.Should().Be(1);
        Item(report, "other-alerts").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Info && a.Count == 3);
    }

    [Fact]
    public void should_say_which_repositories_have_code_scanning_off_and_which_have_pending_dependabot_pull_requests()
    {
        var unscanned = Repository("shop") with { CodeScanning = AlertScan.Off, DependabotPullRequests = 2, DependabotPullsUrl = new Uri("https://github.com/example/shop/pulls/app%2Fdependabot") };

        var report = Build([unscanned, Repository("api")]);

        Item(report, "code-scanning-off").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Info && a.Subjects.Single().Label == "example/shop");
        var pulls = Item(report, "dependabot-prs");
        pulls.Count.Should().Be(2);
        pulls.Subjects.Single().Url!.AbsoluteUri.Should().Be("https://github.com/example/shop/pulls/app%2Fdependabot", "the address comes from the host's connector, Core builds none");
    }

    [Fact]
    public void should_report_a_code_source_that_could_not_be_scanned_as_a_warning()
    {
        var scan = new RepositoryScan([], "rate-limited", Now);
        var parsed = TestCatalog.Parse(Catalog);

        var report = MaintenanceService.Build(parsed.Catalog, new Dictionary<string, EnvironmentInventory>(), new MaintenanceState(scan, new Dictionary<string, RegistryLookup>()));

        Item(report, "code-source-error").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Warning && a.Count == 1 && a.Subjects.Single().Label == "rate-limited");
    }

    [Fact]
    public void should_tell_a_newer_minor_version_from_a_rebuilt_floating_tag_and_an_image_it_could_not_check()
    {
        var registries = new Dictionary<string, RegistryLookup>
        {
            ["docker.io/library/db"] = new([new RegistryTag("16.1"), new RegistryTag("16.2")], null, Now),
            ["docker.io/library/web"] = new([new RegistryTag("latest", Now, "sha256:new")], null, Now),
            ["docker.io/library/cache"] = new([], "refused", Now),
        };

        var report = Build([], registries, Container("db", "db:16.1"), Container("web", "web:latest") with { ImageDigest = "sha256:old" }, Container("cache", "cache:7"));

        Item(report, "images-behind").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Info && a.Subjects.Single().Label == "db:16.1");
        Item(report, "image-rebuilt").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Info && a.Subjects.Single().Label == "web:latest");
        Item(report, "images-unknown").Should().Match<AttentionItem>(a => a.Level == AttentionLevel.Info && a.Subjects.Single().Label == "cache:7");
        report.Attention.Select(a => a.Kind).Should().NotContain("image-major", "16.1 to 16.2 is a minor change");
    }

    [Fact]
    public void should_let_two_apps_share_one_repository()
    {
        var parsed = TestCatalog.Parse("""
            sources:
              github: { owners: [example] }
            apps:
              - { id: shop, repository: example/shop }
              - { id: shop-admin, repository: example/shop }
            """);
        var report = MaintenanceService.Build(parsed.Catalog, new Dictionary<string, EnvironmentInventory>(), new MaintenanceState(new RepositoryScan([Repository("shop")], null, Now), new Dictionary<string, RegistryLookup>()));

        report.Repositories.Single().AppIds.Should().BeEquivalentTo("shop", "shop-admin");
    }

    [Fact]
    public void should_keep_a_repository_nothing_runs_from_as_not_deployed()
    {
        var report = Build([Repository("lonely")], Container("db", "db:1"));

        report.Repositories.Single().Should().Match<RepositoryView>(v => !v.Deployed && v.AppIds.Count == 0 && v.Images.Count == 0);
        report.Summary.DeployedRepositories.Should().Be(0);
    }

    private static MaintenanceReport Build(IReadOnlyList<CodeRepository> repositories, params Workload[] workloads)
    {
        return Build(repositories, new Dictionary<string, RegistryLookup>(), workloads);
    }

    private static MaintenanceReport Build(IReadOnlyList<CodeRepository> repositories, Dictionary<string, RegistryLookup> registries, params Workload[] workloads)
    {
        var parsed = TestCatalog.Parse(Catalog);
        parsed.Errors.Should().BeEmpty();
        var inventory = new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, Now) };
        return MaintenanceService.Build(parsed.Catalog, inventory, new MaintenanceState(new RepositoryScan(repositories, null, Now), registries), null, [new GitHubLikeSource()]);
    }

    private static CodeRepository Repository(string name) => new(
        "example", name, new Uri($"https://github.com/example/{name}"), "public", "C#", null, Now, "main", false, false,
        AlertScan.Off, AlertScan.On(AlertCounts.None), AlertScan.On(AlertCounts.None), null, 0, null);

    private static Workload Container(string name, string image, Dictionary<string, string>? labels = null) =>
        new("server-1", name, name, image, WorkloadState.Running, WorkloadHealth.None, "Up", Now, null, labels ?? new Dictionary<string, string>());

    /// <summary>What a code host's connector tells about its addresses and image names (the real rules are tested with the connector).</summary>
    private sealed class GitHubLikeSource : ICodeRepositorySource
    {
        public bool Supports(ICodeSource source) => true;

        public string? GetConfigurationError(ICodeSource source) => null;

        public Task<IReadOnlyList<CodeRepository>> ScanAsync(ICodeSource source, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CodeRepository>>([]);

        public string? RepositoryOfSourceLabel(string label) => label.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? label["https://github.com/".Length..].Replace(".git", string.Empty, StringComparison.Ordinal).TrimEnd('/') : null;

        public string? RepositoryOfImage(ImageReference image) => image.Registry is "ghcr.io" or ImageReference.DockerHub ? image.Repository : null;
    }
}
