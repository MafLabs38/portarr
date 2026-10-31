using System.Net;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class MetricsFixture
{
    private readonly TestDatabase _database;

    public MetricsFixture(TestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task should_expose_build_info_and_http_metrics_for_prometheus()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = await factory.CreateSignedInClientAsync();
        await client.GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("portarr_build_info{version=");
        body.Should().Contain("http_requests_received_total");
    }
}
