using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The monitor's step for the exploit intelligence (D-133): read once a day after the scanner, for the ids its findings carry, kept when it fails.</summary>
public sealed class MaintenanceThreatIntelFixture
{
    private const string Declared = "imageScanner: { url: \"http://scanner.homelab.example:8489\", allowInsecure: true }\n";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();
    private readonly IImageScanner _scanner = Substitute.For<IImageScanner>();
    private readonly IExploitIntelligence _intel = Substitute.For<IExploitIntelligence>();
    private readonly KnownExploitedCatalog _kev;
    private ScannerReport _report;

    public MaintenanceThreatIntelFixture()
    {
        UseCatalog(Declared);
        var workloads = new List<Workload> { new("server-1", "web", "web", "web:1", WorkloadState.Running, WorkloadHealth.None, "Up", _clock.GetUtcNow(), null, new Dictionary<string, string>()) };
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, _clock.GetUtcNow()) });
        _report = Report("CVE-2021-44228", "cve-2024-0002", "GHSA-aaaa-bbbb-cccc");
        _scanner.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_report));
        _kev = new KnownExploitedCatalog(_clock.GetUtcNow(), new Dictionary<string, KnownExploited> { ["CVE-2021-44228"] = new(new DateOnly(2021, 12, 10), null, false, null, null, null) });
        _intel.ReadKnownExploitedAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_kev));
        _intel.ReadPredictionsAsync(Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ExploitPredictions(_clock.GetUtcNow(), call.Arg<IReadOnlySet<string>>().ToDictionary(id => id, _ => new ExploitPrediction(0.5, 0.5)))));
    }

    [Fact]
    public async Task should_read_the_whole_catalog_and_the_predictions_of_the_cve_ids_the_findings_carry()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _intel.Received(1).ReadPredictionsAsync(Arg.Is<IReadOnlySet<string>>(ids => ids.SetEquals(new[] { "CVE-2021-44228", "CVE-2024-0002" })), Arg.Any<CancellationToken>());
        var intel = monitor.Current.Intel;
        intel.Should().Match<ThreatIntelSnapshot>(i => i.Enabled && i.At == _clock.GetUtcNow() && i.Error == null);
        intel.KnownExploited.Should().BeSameAs(_kev);
        intel.Predictions.Scores.Keys.Should().BeEquivalentTo("CVE-2021-44228", "CVE-2024-0002");
    }

    [Fact]
    public async Task should_read_once_a_day_and_not_again_on_a_manual_refresh()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        monitor.RequestRefresh().Accepted.Should().BeTrue();
        _clock.Advance(TimeSpan.FromHours(23));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _intel.Received(1).ReadKnownExploitedAsync(Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromHours(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _intel.Received(2).ReadKnownExploitedAsync(Arg.Any<CancellationToken>());
        await _intel.Received(2).ReadPredictionsAsync(Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_give_a_new_id_its_prediction_at_the_next_daily_read()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        _report = Report("CVE-2021-44228", "CVE-2026-0001");
        _clock.Advance(MaintenanceMonitor.ScannerInterval);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        monitor.Current.Intel.Predictions.Scores.Should().NotContainKey("CVE-2026-0001");

        _clock.Advance(MaintenanceMonitor.ThreatIntelInterval);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        monitor.Current.Intel.Predictions.Scores.Should().ContainKey("CVE-2026-0001");
    }

    [Fact]
    public async Task should_keep_the_previous_data_with_the_error_and_try_again_an_hour_later()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _intel.ReadKnownExploitedAsync(Arg.Any<CancellationToken>()).Returns<Task<KnownExploitedCatalog>>(_ => throw new HttpRequestException("down", null, System.Net.HttpStatusCode.ServiceUnavailable));
        _intel.ReadPredictionsAsync(Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>()).Returns<Task<ExploitPredictions>>(_ => throw new ExploitIntelligenceUnavailableException(ExploitIntelligenceUnavailableException.InsecureRedirect));

        _clock.Advance(MaintenanceMonitor.ThreatIntelInterval);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        var intel = monitor.Current.Intel;
        intel.Error.Should().Be("http-503", "the first failure is the one shown");
        intel.At.Should().Be(_clock.GetUtcNow());
        intel.KnownExploited.Should().BeSameAs(_kev);
        intel.Predictions.Scores.Should().HaveCount(2);

        _clock.Advance(TimeSpan.FromMinutes(59));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _intel.Received(2).ReadKnownExploitedAsync(Arg.Any<CancellationToken>());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _intel.Received(3).ReadKnownExploitedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_say_timeout_when_a_read_runs_out_of_time()
    {
        _intel.ReadKnownExploitedAsync(Arg.Any<CancellationToken>()).Returns<Task<KnownExploitedCatalog>>(_ => throw new OperationCanceledException());
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Intel.Error.Should().Be(ReasonCodes.Timeout);
        monitor.Current.Intel.KnownExploited.Should().Be(KnownExploitedCatalog.Empty);
    }

    [Fact]
    public async Task should_read_the_predictions_as_soon_as_findings_appear_after_a_read_that_had_none()
    {
        _report = Report();
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _intel.DidNotReceiveWithAnyArgs().ReadPredictionsAsync(default!, TestContext.Current.CancellationToken);
        await _intel.Received(1).ReadKnownExploitedAsync(Arg.Any<CancellationToken>());

        _report = Report("CVE-2021-44228");
        _clock.Advance(MaintenanceMonitor.ScannerInterval);
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _intel.Received(1).ReadPredictionsAsync(Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
        monitor.Current.Intel.Predictions.Scores.Keys.Should().Equal("CVE-2021-44228");
    }

    [Fact]
    public async Task should_read_nothing_when_switched_off()
    {
        var monitor = Monitor(enabled: false);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Intel.Should().BeSameAs(ThreatIntelSnapshot.Off);
        _intel.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_nothing_without_an_image_scanner_whose_findings_it_would_enrich()
    {
        UseCatalog("environments: []\n");
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Intel.Should().BeSameAs(ThreatIntelSnapshot.Off);
        _intel.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_be_off_when_no_connector_is_installed()
    {
        var monitor = new MaintenanceMonitor(_catalog, _inventory, [], [], [_scanner], [], Options.Create(new PortarrOptions()), _clock, NullLogger<MaintenanceMonitor>.Instance);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Intel.Should().BeSameAs(ThreatIntelSnapshot.Off);
    }

    private void UseCatalog(string yaml) => _catalog.Current.Returns(new CatalogState(TestCatalog.Parse(yaml).Catalog, "catalog.yaml", true, [], null));

    private ScannerReport Report(params string[] ids)
    {
        var findings = ids.Select(id => new ImageFinding(id, "openssl", "1", null, "critical", null, null)).ToList();
        var result = new ImageScanResult("docker.io/library/web:1", ImageScanState.Scanned, null, _clock.GetUtcNow(), new VulnerabilityCounts(findings.Count, 0, 0, 0, 0), VulnerabilityCounts.None, findings);
        return new ScannerReport(_clock.GetUtcNow(), null, [result]);
    }

    private MaintenanceMonitor Monitor(bool enabled = true) =>
        new(_catalog, _inventory, [], [], [_scanner], [_intel], Options.Create(new PortarrOptions { ThreatIntel = new ThreatIntelOptions { Enabled = enabled } }), _clock, NullLogger<MaintenanceMonitor>.Instance);
}
