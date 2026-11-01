using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Workloads;

public sealed class WorkloadInventoryFixture
{
    private static readonly CatalogEnvironment Server = TestEnvironments.DockerHost("server-1", "Server 1");
    private static readonly Workload Sonarr = new("server-1", "id", "sonarr", "sonarr", WorkloadState.Running, WorkloadHealth.None, "Up", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());

    private readonly IWorkloadSource _source = Substitute.For<IWorkloadSource>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly WorkloadInventory _subject;

    public WorkloadInventoryFixture()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.Current.Returns(new CatalogState(new Portarr.Core.Catalog.Catalog([Server, new CatalogEnvironment("nas", "NAS", null)], [], []), "catalog.yaml", true, [], null));
        _source.Supports(Arg.Any<CatalogEnvironment>()).Returns(call => call.Arg<CatalogEnvironment>().Declares("docker"));
        _subject = new WorkloadInventory(catalog, [_source], _clock, NullLogger<WorkloadInventory>.Instance);
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new HttpRequestException("Connection refused"), "unreachable" },
        { new TaskCanceledException(), "timeout" },
        { new System.Text.Json.JsonException(), "invalid-response" },
    };

    [Theory]
    [MemberData(nameof(Failures), DisableDiscoveryEnumeration = true)]
    public async Task should_keep_the_last_known_workloads_when_an_environment_fails(Exception failure, string reason)
    {
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns([Sonarr]);
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);
        var lastSeen = _clock.GetUtcNow();

        _clock.Advance(TimeSpan.FromMinutes(1));
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Workload>>(_ => throw failure);
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        var current = _subject.Current["server-1"];
        current.Reachable.Should().BeFalse();
        current.Error.Should().Be(reason);
        current.Workloads.Should().Equal(Sonarr);
        current.RefreshedAt.Should().Be(lastSeen);
    }

    [Fact]
    public async Task should_be_reachable_again_without_error_when_an_environment_recovers()
    {
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Workload>>(_ => throw new HttpRequestException("Connection refused"));
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(1));
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns([Sonarr]);
        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        var current = _subject.Current["server-1"];
        current.Reachable.Should().BeTrue();
        current.Error.Should().BeNull();
        current.Workloads.Should().Equal(Sonarr);
        current.RefreshedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task should_propagate_caller_cancellation_instead_of_recording_a_timeout()
    {
        using var caller = new CancellationTokenSource();
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Workload>>(_ =>
        {
            caller.Cancel();
            throw new TaskCanceledException();
        });

        await FluentActions.Awaiting(() => _subject.RefreshAsync(caller.Token)).Should().ThrowAsync<OperationCanceledException>();
        _subject.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task should_skip_environments_without_a_connector()
    {
        _source.ListAsync(Server, Arg.Any<CancellationToken>()).Returns([Sonarr]);

        await _subject.RefreshAsync(TestContext.Current.CancellationToken);

        _subject.Current.Keys.Should().Equal("server-1");
    }
}
