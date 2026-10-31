using System.Net;
using System.Net.Http.Json;
using Portarr.Api.V1.Maintenance;

namespace Portarr.Integration.Test.Api;

/// <summary>The manual refresh of the Maintenance page (rate limited to once per minute).</summary>
[Trait("Category", "Integration")]
public sealed class MaintenanceRefreshFixture
{
    private readonly TestDatabase _database;

    public MaintenanceRefreshFixture(TestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task should_accept_a_refresh_then_answer_429_with_retry_after_until_a_minute_has_passed()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = await factory.CreateSignedInClientAsync();

        var first = await client.PostAsync("/api/v1/maintenance/refresh", null, TestContext.Current.CancellationToken);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        var second = await client.PostAsync("/api/v1/maintenance/refresh", null, TestContext.Current.CancellationToken);
        factory.Clock.Advance(TimeSpan.FromSeconds(40));
        var third = await client.PostAsync("/api/v1/maintenance/refresh", null, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(40));
        third.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task should_tell_in_the_report_when_the_next_refresh_is_accepted()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = await factory.CreateSignedInClientAsync();

        await client.PostAsync("/api/v1/maintenance/refresh", null, TestContext.Current.CancellationToken);
        var report = await client.GetFromJsonAsync<MaintenanceResource>("/api/v1/maintenance", TestContext.Current.CancellationToken);

        report!.Refresh.AvailableAt.Should().Be(factory.Clock.GetUtcNow().AddSeconds(60));
    }
}
