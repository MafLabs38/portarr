using System.Net;
using System.Net.Http.Json;
using Portarr.Api.V1.System;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class LoggingFixture
{
    private readonly TestDatabase _database;

    public LoggingFixture(TestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task should_default_to_information()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = factory.CreateClient();

        var logging = await client.GetFromJsonAsync<LoggingResource>("/api/v1/system/logging", TestContext.Current.CancellationToken);

        logging!.Level.Should().Be("Information");
    }

    [Fact]
    public async Task should_keep_the_level_across_restarts()
    {
        var options = await _database.CreateOptionsAsync();

        await using (var first = new PortarrFactory(options))
        {
            using var client = first.CreateClient();
            var response = await client.PutAsJsonAsync("/api/v1/system/logging", new LoggingResource("debug"), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<LoggingResource>(TestContext.Current.CancellationToken))!.Level.Should().Be("Debug");
        }

        await using var second = new PortarrFactory(options);
        using var restarted = second.CreateClient();

        var logging = await restarted.GetFromJsonAsync<LoggingResource>("/api/v1/system/logging", TestContext.Current.CancellationToken);

        logging!.Level.Should().Be("Debug");
    }

    [Theory]
    [InlineData("Loud")]
    [InlineData("Critical")]
    public async Task should_reject_an_unknown_or_unsafe_level(string level)
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/v1/system/logging", new LoggingResource(level), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Level must be one of");
    }
}
