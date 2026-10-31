using Portarr.Core.Maintenance;

namespace Portarr.Core.Test.Maintenance;

/// <summary>A running image against what its registry lists (F-UPD-01, D-66): honest when it cannot tell.</summary>
public sealed class ImageCheckFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static RegistryLookup Lookup(params RegistryTag[] tags) => new(tags, null, Now);

    [Fact]
    public void should_report_a_newer_version_with_its_date()
    {
        var check = ImageCheck.Evaluate(ImageReference.Parse("postgres:16-alpine")!, null, null, Lookup(new RegistryTag("16-alpine"), new RegistryTag("18-alpine", Now.AddDays(-4))));

        check.Should().Be(new ImageCheck(ImageStatus.Behind, "16-alpine", "18-alpine", VersionChange.Major, null, Now.AddDays(-4)));
    }

    [Fact]
    public void should_compare_a_floating_tag_by_digest_when_the_runtime_reports_it()
    {
        var image = ImageReference.Parse("nginx:latest")!;
        var lookup = Lookup(new RegistryTag("latest", Now, "sha256:new"));

        ImageCheck.Evaluate(image, null, "sha256:new", lookup).Status.Should().Be(ImageStatus.UpToDate);
        ImageCheck.Evaluate(image, null, "sha256:old", lookup).Status.Should().Be(ImageStatus.NewerBuild);
    }

    [Fact]
    public void should_compare_a_floating_tag_through_its_version_label_only_when_the_label_is_a_tag()
    {
        var image = ImageReference.Parse("example/app:latest")!;
        var lookup = Lookup(new RegistryTag("latest"), new RegistryTag("1.2.0"), new RegistryTag("1.3.0"));

        ImageCheck.Evaluate(image, "1.2.0", null, lookup).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Behind && c.Latest == "1.3.0" && c.FromLabel);

        // A base image's label (an OS version) is not a version of this repository.
        ImageCheck.Evaluate(image, "22.04", null, lookup).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Floating && c.Reason == ImageCheck.FloatingTag);
    }

    [Fact]
    public void should_say_why_it_cannot_tell()
    {
        var id = ImageReference.Parse("sha256:" + new string('b', 64))!;

        ImageCheck.Evaluate(id, "4.0.1", null, null).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Unknown && c.Reason == ImageCheck.ImageId && c.Current == "4.0.1");
        ImageCheck.Evaluate(ImageReference.Parse("app:1")!, null, null, null).Status.Should().Be(ImageStatus.Pending);
        ImageCheck.Evaluate(ImageReference.Parse("ghcr.io/me/app:1")!, null, null, new RegistryLookup([], "refused", Now)).Reason.Should().Be("refused");
    }
}
