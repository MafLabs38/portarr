using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

public sealed class IconNamesFixture
{
    [Theory]
    [InlineData("lscr.io/linuxserver/sonarr:4.0.9", "sonarr")]
    [InlineData("postgres:17-alpine", "postgres")]
    [InlineData("grafana/grafana@sha256:0123abcd", "grafana")]
    [InlineData("ghcr.io/org/my-app", "my-app")]
    [InlineData("registry:5000/team/Jellyfin:10", "jellyfin")]
    public void should_guess_the_icon_from_the_image(string image, string expected)
    {
        IconNames.FromImage(image).Should().Be(expected);
    }

    [Fact]
    public void should_not_guess_from_a_name_that_is_not_a_valid_icon_name()
    {
        IconNames.FromImage("org/weird_name").Should().BeNull();
    }
}
