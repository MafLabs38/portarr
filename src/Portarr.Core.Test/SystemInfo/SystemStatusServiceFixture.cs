using Portarr.Common;
using Portarr.Core.SystemInfo;
using Portarr.Test.Common;

namespace Portarr.Core.Test.SystemInfo;

public sealed class SystemStatusServiceFixture : CoreTest<SystemStatusService>
{
    [Fact]
    public void should_report_start_time_when_created()
    {
        var createdAt = Clock.GetUtcNow();

        Subject.GetStatus().StartTime.Should().Be(createdAt);
    }

    [Fact]
    public void should_report_elapsed_time_as_uptime_when_time_passes()
    {
        _ = Subject;

        Clock.Advance(TimeSpan.FromMinutes(90));

        Subject.GetStatus().Uptime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void should_report_application_identity()
    {
        var status = Subject.GetStatus();

        status.AppName.Should().Be(BuildInfo.AppName);
        status.Version.Should().NotBeNullOrWhiteSpace();
        status.RuntimeVersion.Should().StartWith(".NET");
    }
}
