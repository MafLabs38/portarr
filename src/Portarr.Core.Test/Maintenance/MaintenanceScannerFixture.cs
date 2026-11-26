using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The monitor's step for the image scanner (D-131): told the running images, read every 30 minutes, kept when the scanner is down.</summary>
public sealed class MaintenanceScannerFixture
{
    private const string Declared = "imageScanner: { url: \"http://scanner.homelab.example:8489\", allowInsecure: true }\n";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();
    private readonly IImageScanner _scanner = Substitute.For<IImageScanner>();
    private readonly IImageRegistry _registry = Substitute.For<IImageRegistry>();

    public MaintenanceScannerFixture()
    {
        UseCatalog(Declared);
        Run("proxy-1", "proxy:1", "proxy-2", "proxy:1", "api", "ghcr.io/example/api:2", "built", "sha256:" + new string('a', 64));
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Report(Scanned("docker.io/library/proxy:1"))));
        _registry.ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>()).Returns([new RegistryTag("1")]);
    }

    [Fact]
    public async Task should_tell_the_scanner_each_running_image_once_and_never_an_image_id()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _scanner.Received(1).RequestAsync(
            Arg.Is<IReadOnlyCollection<ScanTarget>>(targets => targets.SequenceEqual(new[] { new ScanTarget("image", "docker.io/library/proxy:1"), new ScanTarget("image", "ghcr.io/example/api:2") })),
            Arg.Any<CancellationToken>());
        monitor.Current.Scans.Results.Keys.Should().Equal("docker.io/library/proxy:1");
        monitor.Current.Scans.Error.Should().BeNull();
        monitor.Current.Scans.At.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task should_ask_the_scanner_every_30_minutes_not_every_minute()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(29));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _scanner.Received(1).ReadAsync(Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _scanner.Received(2).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_ask_again_at_once_on_a_manual_refresh()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(2));
        monitor.RequestRefresh().Accepted.Should().BeTrue();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _scanner.Received(2).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_the_previous_results_and_show_the_error_when_the_scanner_is_down_then_ask_again_in_5_minutes()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns<ScannerReport>(_ => throw new HttpRequestException("down", null, System.Net.HttpStatusCode.BadGateway));

        _clock.Advance(MaintenanceMonitor.ScannerInterval);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Error.Should().Be("http-502");
        monitor.Current.Scans.Results.Should().ContainKey("docker.io/library/proxy:1", "what was known stays next to the error");
        _clock.Advance(MaintenanceMonitor.ScannerRetryInterval - TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _scanner.Received(2).ReadAsync(Arg.Any<CancellationToken>());

        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Report(Scanned("docker.io/library/proxy:1"))));
        _clock.Advance(TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        monitor.Current.Scans.Error.Should().BeNull();
    }

    [Fact]
    public async Task should_name_the_reason_of_a_scanner_that_cannot_be_called()
    {
        _scanner.RequestAsync(Arg.Any<IReadOnlyCollection<ScanTarget>>(), Arg.Any<CancellationToken>()).Returns(_ => throw new ImageScannerUnavailableException(ImageScannerUnavailableException.SecretMissing));
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns<ScannerReport>(_ => throw new ImageScannerUnavailableException(ImageScannerUnavailableException.SecretMissing));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Error.Should().Be("secret-missing");
        monitor.Current.Scans.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_what_the_scanner_knows_even_when_asking_it_to_scan_failed()
    {
        _scanner.RequestAsync(Arg.Any<IReadOnlyCollection<ScanTarget>>(), Arg.Any<CancellationToken>()).Returns(_ => throw new HttpRequestException("busy", null, System.Net.HttpStatusCode.BadGateway));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Error.Should().BeNull("the read is the source of truth");
        monitor.Current.Scans.Results.Should().ContainKey("docker.io/library/proxy:1");
    }

    [Fact]
    public async Task should_forget_what_another_scanner_said_when_the_address_changes()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        UseCatalog("imageScanner: { url: \"http://other-scanner.homelab.example:8489\", allowInsecure: true }\n");
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns<ScannerReport>(_ => throw new HttpRequestException("down"));

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Results.Should().BeEmpty("the new scanner is read at once and nothing of the old one is shown as its own");
        monitor.Current.Scans.Error.Should().Be("unreachable");
    }

    [Fact]
    public async Task should_keep_the_images_and_the_releases_apart_and_drop_any_other_kind()
    {
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Report(Scanned("docker.io/library/proxy:1"), Scanned("release.json") with { Kind = "sbom" }, Scanned("Example/Desktop-App") with { Kind = "release", Version = "v1.0" }, Scanned("example/desktop-app") with { Kind = "release" })));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Results.Keys.Should().Equal("docker.io/library/proxy:1");
        monitor.Current.Scans.Releases.Keys.Should().ContainSingle("a repository name is one whatever its case").Which.Should().BeEquivalentTo("Example/Desktop-App");
        monitor.Current.Scans.Releases["example/desktop-app"].Version.Should().Be("v1.0");
    }

    [Fact]
    public async Task should_be_off_when_no_scanner_is_declared_and_ask_nothing()
    {
        UseCatalog("environments: []\n");
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Should().BeSameAs(ImageScanSnapshot.Off);
        await _scanner.DidNotReceiveWithAnyArgs().RequestAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_be_off_when_the_catalog_declares_a_scanner_but_no_connector_is_installed()
    {
        var monitor = new MaintenanceMonitor(_catalog, _inventory, [], [_registry], [], [], Options.Create(new PortarrOptions()), _clock, NullLogger<MaintenanceMonitor>.Instance);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Scans.Should().BeSameAs(ImageScanSnapshot.Off);
    }

    [Fact]
    public async Task should_not_ask_the_scanner_to_scan_nothing_but_still_read_it()
    {
        Run("built", "sha256:" + new string('a', 64));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _scanner.DidNotReceiveWithAnyArgs().RequestAsync(default!, TestContext.Current.CancellationToken);
        await _scanner.Received(1).ReadAsync(Arg.Any<CancellationToken>());
    }

    private void UseCatalog(string yaml) => _catalog.Current.Returns(new CatalogState(TestCatalog.Parse(yaml).Catalog, "catalog.yaml", true, [], null));

    private void Run(params string[] namesAndImages)
    {
        var workloads = namesAndImages.Chunk(2)
            .Select(pair => new Workload("server-1", pair[0], pair[0], pair[1], WorkloadState.Running, WorkloadHealth.None, "Up", _clock.GetUtcNow(), null, new Dictionary<string, string>()))
            .ToList();
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, _clock.GetUtcNow()) });
    }

    private ScannerReport Report(params ImageScanResult[] images) => new(_clock.GetUtcNow(), _clock.GetUtcNow().AddHours(-3), images);

    private ImageScanResult Scanned(string image) =>
        new(image, ImageScanState.Scanned, null, _clock.GetUtcNow(), new VulnerabilityCounts(0, 1, 0, 0, 0), new VulnerabilityCounts(0, 1, 0, 0, 0), [new ImageFinding("CVE-1", "openssl", "1", "2", "high", null, null)]);

    private MaintenanceMonitor Monitor() =>
        new(_catalog, _inventory, [], [_registry], [_scanner], [], Options.Create(new PortarrOptions()), _clock, NullLogger<MaintenanceMonitor>.Instance);
}
