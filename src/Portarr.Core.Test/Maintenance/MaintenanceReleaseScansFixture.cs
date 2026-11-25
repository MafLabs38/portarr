using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The monitor tells the scanner which repositories' latest release to scan beside the running images (D-132).</summary>
public sealed class MaintenanceReleaseScansFixture
{
    private const string Scanner = "imageScanner: { url: \"http://scanner.homelab.example:8489\", allowInsecure: true }\n";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();
    private readonly ICodeRepositorySource _code = Substitute.For<ICodeRepositorySource>();
    private readonly IImageScanner _scanner = Substitute.For<IImageScanner>();
    private List<ScanTarget> _asked = [];

    public MaintenanceReleaseScansFixture()
    {
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory>());
        _code.Supports(Arg.Any<ICodeSource>()).Returns(true);
        _code.GetConfigurationError(Arg.Any<ICodeSource>()).Returns((string?)null);
        _scanner.RequestAsync(Arg.Any<IReadOnlyCollection<ScanTarget>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _asked = [.. call.Arg<IReadOnlyCollection<ScanTarget>>()];
            return Task.CompletedTask;
        });
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new ScannerReport(_clock.GetUtcNow(), null, [])));
    }

    [Fact]
    public async Task should_ask_for_the_latest_release_of_each_repository_that_publishes_one_and_leave_out_the_rest()
    {
        UseCatalog(Scanner + "sources:\n  github: { owners: [example] }\n");
        Scan(
            Repo("example", "desktop-app", release: Release("v1.0.0")),
            Repo("example", "tagged", release: Release("v0.1.0", tagOnly: true)),
            Repo("example", "no-release"),
            Repo("example", "archived", release: Release("v2"), archived: true),
            Repo("example", "forked", release: Release("v3"), fork: true),
            Repo("example", "library", release: Release("v4")));

        await Monitor().RefreshAsync(TestContext.Current.CancellationToken);

        _asked.Should().Equal(new ScanTarget("release", "example/desktop-app"), new ScanTarget("release", "example/library"));
    }

    [Fact]
    public async Task should_read_the_release_of_the_mapped_repository_and_not_ask_for_the_releases_repository_itself()
    {
        UseCatalog(Scanner + "sources:\n  github: { owners: [example], releases: { example/app-source: example/app-releases } }\n");
        Scan(
            Repo("example", "app-source", visibility: "private"),
            Repo("example", "app-releases", release: Release("v1.2.3")),
            Repo("example", "other", release: Release("v1")));

        await Monitor().RefreshAsync(TestContext.Current.CancellationToken);

        _asked.Should().Equal(new ScanTarget("release", "example/app-releases"), new ScanTarget("release", "example/other"));
    }

    [Fact]
    public async Task should_ask_for_a_mapped_release_even_when_its_repository_is_not_among_the_scanned_ones_but_not_for_an_archived_source()
    {
        UseCatalog(Scanner + "sources:\n  github: { owners: [example], archived: true, releases: { Example/Live: other/public-releases, example/old: other/old-releases } }\n");
        Scan(Repo("example", "live"), Repo("example", "old", archived: true));

        await Monitor().RefreshAsync(TestContext.Current.CancellationToken);

        _asked.Should().Equal(new ScanTarget("release", "other/public-releases"));
    }

    [Fact]
    public async Task should_ask_once_for_a_release_two_repositories_share_and_send_it_with_the_images()
    {
        UseCatalog(Scanner + "sources:\n  github: { owners: [example], releases: { example/a: other/shared, example/b: other/shared } }\n");
        Scan(Repo("example", "a"), Repo("example", "b"));
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory>
        {
            ["server-1"] = new("server-1", true, null, [new Workload("server-1", "proxy", "proxy", "proxy:1", WorkloadState.Running, WorkloadHealth.None, "Up", _clock.GetUtcNow(), null, new Dictionary<string, string>())], _clock.GetUtcNow()),
        });

        await Monitor().RefreshAsync(TestContext.Current.CancellationToken);

        _asked.Should().Equal(new ScanTarget("image", "docker.io/library/proxy:1"), new ScanTarget("release", "other/shared"));
        await _scanner.Received(1).RequestAsync(Arg.Any<IReadOnlyCollection<ScanTarget>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_ask_for_no_release_when_no_scanner_is_declared()
    {
        UseCatalog("sources:\n  github: { owners: [example] }\n");
        Scan(Repo("example", "desktop-app", release: Release("v1.0.0")));

        await Monitor().RefreshAsync(TestContext.Current.CancellationToken);

        await _scanner.DidNotReceiveWithAnyArgs().RequestAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_still_read_the_scanner_when_asking_for_a_release_failed_and_keep_the_results_by_repository()
    {
        UseCatalog(Scanner + "sources:\n  github: { owners: [example] }\n");
        Scan(Repo("example", "desktop-app", release: Release("v1.0.0")));
        _scanner.RequestAsync(Arg.Any<IReadOnlyCollection<ScanTarget>>(), Arg.Any<CancellationToken>()).Returns(_ => throw new HttpRequestException("busy"));
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new ScannerReport(_clock.GetUtcNow(), null, [
            new ImageScanResult("example/desktop-app", ImageScanState.Scanned, null, _clock.GetUtcNow(), new VulnerabilityCounts(0, 1, 0, 0, 0), new VulnerabilityCounts(0, 1, 0, 0, 0), [], ScanTargetKinds.Release, "v1.0.0")])));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Error.Should().BeNull();
        monitor.Current.Scans.Releases.Should().ContainKey("example/desktop-app").WhoseValue.Version.Should().Be("v1.0.0");
        monitor.Current.Scans.Results.Should().BeEmpty();
    }

    private void UseCatalog(string yaml) => _catalog.Current.Returns(new CatalogState(TestCatalog.Parse(yaml).Catalog, "catalog.yaml", true, [], null));

    private void Scan(params CodeRepository[] repositories) =>
        _code.ScanAsync(Arg.Any<ICodeSource>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<CodeRepository>>(repositories));

    private MaintenanceMonitor Monitor() =>
        new(_catalog, _inventory, [_code], [], [_scanner], Options.Create(new PortarrOptions()), _clock, NullLogger<MaintenanceMonitor>.Instance);

    private static ReleaseInfo Release(string tag, bool tagOnly = false) => new(tag, null, null, tagOnly);

    internal static CodeRepository Repo(string owner, string name, ReleaseInfo? release = null, bool archived = false, bool fork = false, string visibility = "public") =>
        new(owner, name, new Uri($"https://github.example/{owner}/{name}"), visibility, null, null, null, "main", archived, fork, AlertScan.Off, AlertScan.Off, AlertScan.Off, release, null, null);
}
