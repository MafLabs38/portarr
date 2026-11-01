using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Configuration;

public sealed class PortarrOptionsFixture
{
    [Fact]
    public void should_not_touch_the_home_directory_when_the_data_folder_is_set()
    {
        var home = Path.Combine(Path.GetTempPath(), $"portarr-home-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", home);
        try
        {
            var options = new PortarrOptions { DataFolder = "/config" };

            options.DataFolder.Should().Be("/config");
            Directory.Exists(home).Should().BeFalse("a read-only root file system must not be written for a default that is not used");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
        }
    }

    [Fact]
    public void should_fall_back_to_a_per_user_folder_when_nothing_sets_it()
    {
        new PortarrOptions().DataFolder.Should().EndWith("Portarr");
    }
}
