using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The slow cadences that keep GitHub and the registries well under their rate limits (D-64, D-66).</summary>
public sealed class MaintenanceMonitorFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly ICodeRepositorySource _code = Substitute.For<ICodeRepositorySource>();
    private readonly IImageRegistry _registry = Substitute.For<IImageRegistry>();
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();

    public MaintenanceMonitorFixture()
    {
        var parsed = TestCatalog.Parse("sources:\n  github: { owners: [example] }\n");
        _catalog.Current.Returns(new CatalogState(parsed.Catalog, "catalog.yaml", true, [], null));
        var workloads = new[] { "proxy-1", "proxy-2" }
            .Select(name => new Workload("server-1", name, name, "proxy:1", WorkloadState.Running, WorkloadHealth.None, "Up", _clock.GetUtcNow(), null, new Dictionary<string, string>()))
            .ToList();
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, _clock.GetUtcNow()) });
        _code.GetConfigurationError(Arg.Any<GitHubSource>()).Returns((string?)null);
        _code.ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>()).Returns([]);
        _registry.ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>()).Returns([new RegistryTag("1"), new RegistryTag("2")]);
    }

    [Fact]
    public async Task should_scan_repositories_every_30_minutes()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(29));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _code.Received(1).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _code.Received(2).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_look_up_each_image_repository_once_every_6_hours()
    {
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromHours(5));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _registry.Received(1).ListTagsAsync(Arg.Is<ImageReference>(i => i.RepositoryKey == "docker.io/library/proxy"), Arg.Any<CancellationToken>());
        monitor.Current.Registries["docker.io/library/proxy"].Tags.Should().HaveCount(2);

        _clock.Advance(TimeSpan.FromHours(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _registry.Received(2).ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_scan_again_in_5_minutes_after_a_failed_scan_not_in_30()
    {
        _code.ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CodeRepository>>(_ => throw new HttpRequestException("down", null, System.Net.HttpStatusCode.BadGateway));
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        monitor.Refresh.GitHubNextAt.Should().Be(_clock.GetUtcNow() + MaintenanceMonitor.RepositoryRetryInterval);

        _clock.Advance(MaintenanceMonitor.RepositoryRetryInterval - TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _code.Received(1).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _code.Received(2).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_leave_a_refresh_under_way_when_the_pass_fails()
    {
        _registry.ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<RegistryTag>>(_ => throw new HttpRequestException("down", null, System.Net.HttpStatusCode.BadGateway));
        _code.ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CodeRepository>>(_ => throw new InvalidProgramException("a bug in a connector"));
        var monitor = Monitor();
        monitor.RequestRefresh();
        monitor.Refresh.Refreshing.Should().BeTrue();

        var pass = () => monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await pass.Should().NotThrowAsync("a failed scan is caught and shown, the pass goes on");
        monitor.Refresh.Refreshing.Should().BeFalse("the request was answered, even by a pass that failed");
    }

    [Fact]
    public async Task should_keep_the_last_repositories_and_say_why_when_a_scan_fails()
    {
        var repository = new CodeRepository("example", "shop", new Uri("https://github.com/example/shop"), "public", null, null, null, "main", false, false, AlertScan.Off, AlertScan.Off, AlertScan.Off, null, null, null);
        var calls = 0;
        _code.ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>()).Returns(_ => ++calls == 1
            ? Task.FromResult<IReadOnlyList<CodeRepository>>([repository])
            : throw new HttpRequestException("down", null, System.Net.HttpStatusCode.BadGateway));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(31));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Repositories.Should().Match<RepositoryScan>(s => s.Error == "http-502" && s.Repositories.Single().Name == "shop");
    }

    [Fact]
    public async Task should_report_a_missing_token_without_calling_github()
    {
        _code.GetConfigurationError(Arg.Any<GitHubSource>()).Returns("token-missing");
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Repositories.Error.Should().Be("token-missing");
        await _code.DidNotReceive().ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_rescan_now_when_a_refresh_is_requested_even_inside_the_cadences()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        monitor.RequestRefresh().Accepted.Should().BeTrue();
        monitor.Refresh.Refreshing.Should().BeTrue();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await _code.Received(2).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
        await _registry.Received(2).ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>());
        monitor.Refresh.Refreshing.Should().BeFalse();

        // The scan that answered the request is not repeated by the next pass.
        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        await _code.Received(2).ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_accept_a_refresh_once_every_60_seconds_only()
    {
        var monitor = Monitor();
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        var first = monitor.RequestRefresh();
        _clock.Advance(TimeSpan.FromSeconds(59));
        var tooSoon = monitor.RequestRefresh();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var later = monitor.RequestRefresh();

        first.Accepted.Should().BeTrue();
        tooSoon.Should().Match<RefreshRequest>(r => !r.Accepted && r.RetryAt == first.RetryAt);
        later.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task should_say_why_when_a_registry_cannot_be_read_and_try_again_in_an_hour()
    {
        _registry.ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<RegistryTag>>(_ => throw new RegistryUnavailableException("rate-limited"));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        var failed = monitor.Current.Registries["docker.io/library/proxy"];
        _clock.Advance(MaintenanceMonitor.RegistryRetryInterval - TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        failed.Error.Should().Be("rate-limited");
        failed.Tags.Should().BeEmpty();
        await _registry.Received(2).ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_no_repository_when_the_catalog_declares_no_code_source()
    {
        _catalog.Current.Returns(new CatalogState(TestCatalog.Parse("apps: []\n").Catalog, "catalog.yaml", true, [], null));
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Repositories.Should().Be(RepositoryScan.NotConfigured);
        await _code.DidNotReceive().ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>());
        monitor.Refresh.GitHubNextAt.Should().BeNull();
    }

    [Fact]
    public async Task should_stop_when_cancelled_instead_of_recording_a_failure()
    {
        using var cancellation = new CancellationTokenSource();
        _code.ScanAsync(Arg.Any<GitHubSource>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CodeRepository>>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var monitor = Monitor();

        var pass = () => monitor.RefreshAsync(cancellation.Token);

        await pass.Should().ThrowAsync<OperationCanceledException>();
        monitor.Current.Repositories.Error.Should().BeNull("a cancelled scan is not an error to show");
    }

    [Fact]
    public async Task should_leave_an_image_it_cannot_parse_out_of_the_lookups()
    {
        var odd = new Workload("server-1", "odd", "odd", "", WorkloadState.Running, WorkloadHealth.None, "Up", _clock.GetUtcNow(), null, new Dictionary<string, string>());
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, [odd], _clock.GetUtcNow()) });
        var monitor = Monitor();

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Registries.Should().BeEmpty();
        await _registry.DidNotReceive().ListTagsAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>());
    }

    private MaintenanceMonitor Monitor() => new(_catalog, _inventory, [_code], [_registry], _clock, NullLogger<MaintenanceMonitor>.Instance);
}
