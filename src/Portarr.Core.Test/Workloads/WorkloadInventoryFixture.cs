using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Workloads;

public sealed class WorkloadInventoryFixture
{
    private static readonly CatalogEnvironment Server = new("server-1", "Server 1", new DockerEndpoint(new Uri("http://proxy:2375")));
    private static readonly Workload Sonarr = new("server-1", "id", "sonarr", "sonarr", WorkloadState.Running, WorkloadHealth.None, "Up", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());

    private readonly IWorkloadSource _source = Substitute.For<IWorkloadSource>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly WorkloadInventory _subject;

    public WorkloadInventoryFixture()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.Current.Returns(new CatalogState(new Portarr.Core.Catalog.Catalog([Server, new CatalogEnvironment("nas", "NAS", null)], [], []), "catalog.yaml", true, [], null));
        _source.Supports(Arg.Any<CatalogEnvironment>()).Returns(call => call.Arg<CatalogEnvironment>().Docker is not null);
        _subject = new WorkloadInventory(catalog, [_source], _clock, NullLogger<WorkloadInventory>.Instance);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), "unreachable")]
    [InlineData(typeof(TaskCanceledException), "timeout")]
    [InlineData(typeof(System.Text.Json.JsonException), "invalid-response")]
    public async Task should_keep_the_last_known_workloads_when_an_environment_fails(Type failure, string reason)
    {
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns([Sonarr]);
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);
        var lastSeen = _clock.GetUtcNow();

        _clock.Advance(TimeSpan.FromMinutes(1));
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Workload>>(_ => throw (Exception)Activator.CreateInstance(failure)!);
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        _subject.Current["server-1"].Should().Be(new EnvironmentInventory("server-1", false, reason, [Sonarr], lastSeen) with { Workloads = _subject.Current["server-1"].Workloads });
        _subject.Current["server-1"].Workloads.Should().Equal(Sonarr);
    }

    [Fact]
    public async Task should_skip_environments_without_a_connector()
    {
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns([Sonarr]);

        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        _subject.Current.Keys.Should().Equal("server-1");
    }
}
