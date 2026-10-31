using Portarr.Core.Maintenance;

namespace Portarr.Core.Test.Maintenance;

/// <summary>Image tags read as versions, compared only with tags of the same shape (F-UPD-01, D-66).</summary>
public sealed class VersionTagFixture
{
    [Theory]
    [InlineData("16-alpine", new[] { "17-alpine", "18-alpine", "18", "18.1-alpine", "19-rc1-alpine", "amd64-19-alpine" }, "18-alpine", VersionChange.Major)]
    [InlineData("4.0.20.3014-ls1", new[] { "4.0.20.3014-ls2", "4.0.19.9999-ls9", "develop-4.1.0.1-ls1" }, "4.0.20.3014-ls2", VersionChange.Build)]
    [InlineData("v1.10.3", new[] { "v1.10.4", "v1.12.0", "1.13.0", "v2.0.0-rc1" }, "v1.12.0", VersionChange.Minor)]
    [InlineData("1.108.2", new[] { "1.108.3", "nightly", "latest" }, "1.108.3", VersionChange.Patch)]
    [InlineData("version-6.13.9", new[] { "version-7.0.0", "7.1.0" }, "version-7.0.0", VersionChange.Major)]
    public void should_find_the_newest_tag_of_the_same_shape(string current, string[] tags, string newest, VersionChange change)
    {
        var version = VersionTag.Parse(current)!;

        var found = version.NewestIn(tags);

        found!.Tag.Should().Be(newest);
        version.ChangeTo(found).Should().Be(change);
    }

    [Fact]
    public void should_find_nothing_newer_for_the_newest_tag()
    {
        VersionTag.Parse("2.7")!.NewestIn(["2.6", "2.7", "2.7-alpine", "3"]).Should().BeNull();
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("stable")]
    [InlineData("alpine")]
    [InlineData("")]
    [InlineData(null)]
    public void should_read_floating_tags_as_no_version(string? tag)
    {
        VersionTag.Parse(tag).Should().BeNull();
    }
}
