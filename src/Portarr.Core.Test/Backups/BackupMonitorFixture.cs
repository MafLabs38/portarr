using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Backups;

/// <summary>The monitor that asks the backup tools the declared data names (D-62): which tools, and why one could not be read.</summary>
public sealed class BackupMonitorFixture
{
    private const string Catalog = """
        environments:
          - { id: server-1, docker: { url: "http://10.0.0.10:2375" }, backrest: { url: "http://10.0.0.10:9898" } }
          - { id: server-2, docker: { url: "http://10.0.0.11:2375" } }
          - { id: cluster, kubernetes: { server: "https://10.0.0.20:6443" } }
          - { id: unused, docker: { url: "http://10.0.0.12:2375" }, backrest: { url: "http://10.0.0.12:9898" } }
        apps:
          - { id: shop }
        data:
          - id: files
            app: shop
            kind: files
            importance: comfort
            where: { environment: server-1 }
            targets:
              local: { backrest: { plan: files } }
              offsite: { backrest: { plan: files-offsite, environment: server-2 } }
          - id: db
            app: shop
            kind: postgresql
            importance: critical
            where: { environment: cluster }
            targets:
              local: { cronjob: shop/dump, k8up: { schedule: shop/offsite } }
        """;

    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public async Task should_read_only_the_tools_the_declared_data_names()
    {
        var backrest = Source(BackupSourceKinds.Backrest, new BackupJob("files", "files", null, [], null));
        var cronjob = Source(BackupSourceKinds.CronJob);
        var k8up = Source(BackupSourceKinds.K8up);
        var monitor = Monitor(backrest, cronjob, k8up);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings.Keys.Should().BeEquivalentTo("backrest@server-1", "backrest@server-2", "cronjob@cluster", "k8up@cluster");
        await backrest.Received(1).ListJobsAsync(Arg.Is<CatalogEnvironment>(e => e.Id == "server-1"), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await backrest.DidNotReceive().ListJobsAsync(Arg.Is<CatalogEnvironment>(e => e.Id == "unused"), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        monitor.Current.Readings["backrest@server-1"].Should().BeEquivalentTo(new SourceReading("backrest", "server-1", [new BackupJob("files", "files", null, [], null)], null, Now));
    }

    [Fact]
    public async Task should_say_no_connector_when_no_source_reads_the_tool_on_that_environment()
    {
        var monitor = Monitor(Source(BackupSourceKinds.Backrest), Source(BackupSourceKinds.CronJob), Source(BackupSourceKinds.K8up));

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        // server-2 has no Backrest endpoint.
        monitor.Current.Readings["backrest@server-2"].Error.Should().Be(BackupMonitor.NoConnector);
        monitor.Current.Readings["backrest@server-1"].Error.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_the_reason_of_a_tool_that_cannot_be_read_without_hiding_the_others()
    {
        var backrest = Source(BackupSourceKinds.Backrest);
        var cronjob = Source(BackupSourceKinds.CronJob);
        cronjob.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw new BackupSourceUnavailableException("token-missing"));
        var k8up = Source(BackupSourceKinds.K8up);
        k8up.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw new HttpRequestException("refused", null, System.Net.HttpStatusCode.Forbidden));
        var monitor = Monitor(backrest, cronjob, k8up);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["cronjob@cluster"].Error.Should().Be("token-missing");
        monitor.Current.Readings["k8up@cluster"].Error.Should().Be("http-403");
        monitor.Current.Readings["backrest@server-1"].Error.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_carry_on_the_reading_whether_the_operator_has_something_to_set_up(bool notSetUp)
    {
        var cronjob = Source(BackupSourceKinds.CronJob);
        cronjob.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw new BackupSourceUnavailableException("a-reason", notSetUp));
        var monitor = Monitor(Source(BackupSourceKinds.Backrest), cronjob, Source(BackupSourceKinds.K8up));

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["cronjob@cluster"].Should().Match<SourceReading>(r => r.Error == "a-reason" && r.NotSetUp == notSetUp);
    }

    [Fact]
    public async Task should_mark_a_missing_connector_as_something_to_set_up()
    {
        var monitor = Monitor(Source(BackupSourceKinds.Backrest), Source(BackupSourceKinds.CronJob), Source(BackupSourceKinds.K8up));

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["backrest@server-2"].Should().Match<SourceReading>(r => r.Error == BackupMonitor.NoConnector && r.NotSetUp);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "timeout")]
    [InlineData(typeof(System.Text.Json.JsonException), "invalid-response")]
    public async Task should_turn_any_other_failure_into_its_reason_code_at_the_monitor(Type failure, string reason)
    {
        var cronjob = Source(BackupSourceKinds.CronJob);
        cronjob.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw (Exception)Activator.CreateInstance(failure)!);
        var monitor = Monitor(Source(BackupSourceKinds.Backrest), cronjob, Source(BackupSourceKinds.K8up));

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["cronjob@cluster"].Should().Match<SourceReading>(r => r.Error == reason && !r.NotSetUp);
    }

    [Fact]
    public async Task should_not_abort_the_refresh_for_an_environment_the_catalog_no_longer_has()
    {
        var catalogService = Substitute.For<ICatalogService>();
        var parsed = TestCatalog.Parse(Catalog);
        // The data still names server-2's backrest, but the environment is gone from the catalog (a reload in between).
        var withoutServer2 = parsed.Catalog with { Environments = parsed.Catalog.Environments.Where(e => e.Id != "server-2").ToList() };
        catalogService.Current.Returns(new CatalogState(withoutServer2, "/config/catalog.yaml", true, [], Now));
        var monitor = new BackupMonitor(catalogService, [Source(BackupSourceKinds.Backrest), Source(BackupSourceKinds.CronJob), Source(BackupSourceKinds.K8up)], _clock, NullLogger<BackupMonitor>.Instance);

        var refresh = () => monitor.RefreshAsync(TestContext.Current.CancellationToken);

        await refresh.Should().NotThrowAsync();
        monitor.Current.Readings["backrest@server-2"].Should().Match<SourceReading>(r => r.Error == BackupMonitor.NoConnector);
    }

    [Fact]
    public async Task should_go_on_after_a_refresh_that_throws_and_stop_when_cancelled()
    {
        var calls = 0;
        var backrest = Source(BackupSourceKinds.Backrest);
        backrest.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => ++calls == 1 ? throw new InvalidProgramException("a bug") : []);
        var monitor = Monitor(backrest, Source(BackupSourceKinds.CronJob), Source(BackupSourceKinds.K8up));
        using var cancellation = new CancellationTokenSource();

        await monitor.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => calls >= 1);
        _clock.Advance(BackupMonitor.Interval);
        await WaitUntilAsync(() => calls >= 2);
        await cancellation.CancelAsync();
        await monitor.StopAsync(TestContext.Current.CancellationToken);

        calls.Should().BeGreaterThanOrEqualTo(2, "the loop survived the first refresh throwing");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for the monitor.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task should_start_with_nothing_read_and_replace_the_readings_at_each_refresh()
    {
        var backrest = Source(BackupSourceKinds.Backrest);
        var monitor = Monitor(backrest, Source(BackupSourceKinds.CronJob), Source(BackupSourceKinds.K8up));
        monitor.Current.Should().BeSameAs(BackupMonitorState.Empty);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["backrest@server-1"].CheckedAt.Should().Be(Now.AddMinutes(1));
    }

    private BackupMonitor Monitor(params IBackupSource[] sources)
    {
        var parsed = TestCatalog.Parse(Catalog);
        parsed.Errors.Should().BeEmpty();
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(parsed.Catalog, "/config/catalog.yaml", true, [], Now));
        return new BackupMonitor(catalogService, sources, _clock, NullLogger<BackupMonitor>.Instance);
    }

    private static IBackupSource Source(string kind, params BackupJob[] jobs)
    {
        var source = Substitute.For<IBackupSource>();
        source.Kind.Returns(kind);
        source.Supports(Arg.Any<CatalogEnvironment>()).Returns(call => kind switch
        {
            BackupSourceKinds.Backrest => call.Arg<CatalogEnvironment>().Declares("backrest"),
            _ => call.Arg<CatalogEnvironment>().Declares("kubernetes"),
        });
        source.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(jobs);
        return source;
    }
}
