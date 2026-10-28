using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portarr.Core.Datastore;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Datastore;

public sealed class DatabaseHealthCheckFixture : CoreTest<DatabaseHealthCheck>
{
    [Fact]
    public async Task should_be_healthy_with_the_engine_version_when_database_answers()
    {
        Mocker.Get<IDatabase>().GetEngineVersionAsync(Arg.Any<CancellationToken>()).Returns("PostgreSQL 17.2");

        var result = await Subject.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("PostgreSQL 17.2");
    }

    [Fact]
    public async Task should_be_unhealthy_when_database_is_unreachable()
    {
        Mocker.Get<IDatabase>().GetEngineVersionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("connection refused")));

        var result = await Subject.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }
}
