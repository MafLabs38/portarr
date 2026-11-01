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
        await backrest.Received(1).ListJobsAsync(Arg.Is<CatalogEnvironment>(e => e.Id == "server-1"), Arg.Any<CancellationToken>());
        await backrest.DidNotReceive().ListJobsAsync(Arg.Is<CatalogEnvironment>(e => e.Id == "unused"), Arg.Any<CancellationToken>());
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
        cronjob.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw new BackupSourceUnavailableException("token-missing"));
        var k8up = Source(BackupSourceKinds.K8up);
        k8up.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BackupJob>>(_ => throw new HttpRequestException("refused", null, System.Net.HttpStatusCode.Forbidden));
        var monitor = Monitor(backrest, cronjob, k8up);

        await monitor.RefreshAsync(TestContext.Current.CancellationToken);

        monitor.Current.Readings["cronjob@cluster"].Error.Should().Be("token-missing");
        monitor.Current.Readings["k8up@cluster"].Error.Should().Be("http-403");
        monitor.Current.Readings["backrest@server-1"].Error.Should().BeNull();
    }

    [Theory]
    [InlineData("no-connector", true)]
    [InlineData("credentials-missing", true)]
    [InlineData("token-missing", true)]
    [InlineData("ca-missing", true)]
    [InlineData("ca-invalid", true)]
    [InlineData("http-403", false)]
    [InlineData("http-401", false)]
    [InlineData("unreachable", false)]
    [InlineData("timeout", false)]
    public void should_tell_a_configuration_to_fix_from_a_tool_that_failed(string reason, bool notSetUp)
    {
        BackupMonitor.IsNotSetUp(reason).Should().Be(notSetUp);
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
            _ => call.Arg<CatalogEnvironment>().Kubernetes is not null,
        });
        source.ListJobsAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns(jobs);
        return source;
    }
}
