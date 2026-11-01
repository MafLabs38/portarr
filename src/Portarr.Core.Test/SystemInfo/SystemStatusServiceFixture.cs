using Microsoft.Extensions.Options;
using Portarr.Common;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;
using Portarr.Core.SystemInfo;
using Portarr.Test.Common;

namespace Portarr.Core.Test.SystemInfo;

public sealed class SystemStatusServiceFixture : CoreTest<SystemStatusService>
{
    public SystemStatusServiceFixture()
    {
        Mocker.Use(Options.Create(new PortarrOptions { DataFolder = "/data/portarr" }));
        Mocker.Get<IDatabase>().GetEngineVersionAsync(Arg.Any<CancellationToken>()).Returns("SQLite 3.46.1");
    }

    [Fact]
    public async Task should_report_start_time_when_created()
    {
        var createdAt = Clock.GetUtcNow();

        var status = await Subject.GetStatusAsync(TestContext.Current.CancellationToken);

        status.StartTime.Should().Be(createdAt);
    }

    [Fact]
    public async Task should_report_elapsed_time_as_uptime_when_time_passes()
    {
        _ = Subject;

        Clock.Advance(TimeSpan.FromMinutes(90));
        var status = await Subject.GetStatusAsync(TestContext.Current.CancellationToken);

        status.Uptime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public async Task should_report_application_identity()
    {
        var status = await Subject.GetStatusAsync(TestContext.Current.CancellationToken);

        status.AppName.Should().Be(BuildInfo.AppName);
        status.Version.Should().NotBeNullOrWhiteSpace();
        status.RuntimeVersion.Should().StartWith(".NET");
    }

    [Fact]
    public async Task should_report_database_engine_and_data_folder()
    {
        var status = await Subject.GetStatusAsync(TestContext.Current.CancellationToken);

        status.DatabaseEngine.Should().Be("SQLite 3.46.1");
        status.DataFolder.Should().Be("/data/portarr");
    }
}
