using System.Net;
using System.Net.Http.Json;
using Portarr.Api.V1.System;
using Portarr.Core.Configuration;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class SystemStatusFixture : IAsyncLifetime
{
    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;

    public SystemStatusFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _factory = new PortarrFactory(await _database.CreateOptionsAsync());
    }

    public ValueTask DisposeAsync()
    {
        return _factory.DisposeAsync();
    }

    [Fact]
    public async Task should_return_status_with_uptime_from_the_host_clock()
    {
        using var client = _factory.CreateClient();
        var first = await GetStatusAsync(client);

        _factory.Clock.Advance(TimeSpan.FromSeconds(42));
        var second = await GetStatusAsync(client);

        second.AppName.Should().Be("Portarr");
        second.StartTime.Should().Be(first.StartTime);
        (second.UptimeSeconds - first.UptimeSeconds).Should().Be(42);
    }

    [Fact]
    public async Task should_report_the_database_in_use()
    {
        using var client = _factory.CreateClient();

        var status = await GetStatusAsync(client);

        status.DatabaseEngine.Should().StartWith(_database.Type == DatabaseType.Postgres ? "PostgreSQL " : "SQLite ");
        status.DataFolder.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_return_not_found_when_api_route_is_unknown()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/unknown", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<SystemStatusResource> GetStatusAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<SystemStatusResource>(
            "/api/v1/system/status", TestContext.Current.CancellationToken);

        return status ?? throw new InvalidOperationException("Empty status response.");
    }
}
