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

    [Fact]
    public void should_find_nothing_newer_in_a_list_of_older_tags_only()
    {
        VersionTag.Parse("2.7.1")!.NewestIn(["2.7.0", "2.6.9", "1.0.0"]).Should().BeNull();
    }

    [Fact]
    public void should_compare_a_current_tag_that_carries_a_pre_release_suffix_with_the_numbers_first()
    {
        var rc = VersionTag.Parse("2.0.0-rc1")!;

        rc.NewestIn(["2.0.0-rc2", "2.0.0-rc10", "2.0.1-rc1"])!.Tag.Should().Be("2.0.1-rc1", "the numbers decide before the suffix");
        rc.NewestIn(["2.0.0-rc2", "2.0.0-rc10"])!.Tag.Should().Be("2.0.0-rc10", "the numbers of the suffix compare as numbers, 10 after 2");
    }

    [Fact]
    public void should_order_tags_of_the_same_shape_and_put_a_missing_tag_first()
    {
        var older = VersionTag.Parse("1.9.0")!;
        var newer = VersionTag.Parse("1.10.0")!;

        older.CompareTo(newer).Should().BeNegative("1.10 is after 1.9");
        newer.CompareTo(older).Should().BePositive();
        older.CompareTo(VersionTag.Parse("1.9.0")).Should().Be(0);
        older.CompareTo(null).Should().BePositive();
        new[] { newer, older }.Order().First().Should().Be(older, "VersionTag is IComparable");
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
