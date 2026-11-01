using System.Net;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class HealthFixture
{
    private readonly TestDatabase _database;

    public HealthFixture(TestDatabase database)
    {
        _database = database;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task should_be_healthy_when_the_database_answers(string path)
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("Healthy");
    }

    [Fact]
    public async Task should_refuse_to_start_when_postgres_has_no_connection_string()
    {
        var options = await _database.CreateOptionsAsync();
        options.Database = new DatabaseOptions { Type = DatabaseType.Postgres };
        await using var factory = new PortarrFactory(options);

        var start = () => factory.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*PORTARR__DATABASE__CONNECTIONSTRING*");
    }
}
