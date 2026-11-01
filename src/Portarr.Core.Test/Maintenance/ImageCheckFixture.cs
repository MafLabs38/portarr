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
    public void should_say_an_image_id_cannot_be_looked_up_but_keep_the_version_its_label_states()
    {
        var id = ImageReference.Parse("sha256:" + new string('b', 64))!;

        ImageCheck.Evaluate(id, "4.0.1", null, null).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Unknown && c.Reason == ImageCheck.ImageId && c.Current == "4.0.1");
    }

    [Fact]
    public void should_wait_for_a_lookup_that_has_not_happened_yet()
    {
        ImageCheck.Evaluate(ImageReference.Parse("app:1")!, null, null, null).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Pending && c.Current == "1");
    }

    [Fact]
    public void should_give_the_reason_a_registry_could_not_be_read()
    {
        ImageCheck.Evaluate(ImageReference.Parse("ghcr.io/me/app:1")!, null, null, new RegistryLookup([], "refused", Now)).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Unknown && c.Reason == "refused");
    }

    [Fact]
    public void should_be_up_to_date_when_no_tag_of_the_same_shape_is_newer()
    {
        var check = ImageCheck.Evaluate(ImageReference.Parse("postgres:18-alpine")!, null, null, Lookup(new RegistryTag("16-alpine"), new RegistryTag("18-alpine", Now)));

        check.Should().Be(new ImageCheck(ImageStatus.UpToDate, "18-alpine", CurrentAt: Now));
    }

    [Fact]
    public void should_not_compare_a_tag_with_tags_of_another_shape()
    {
        var check = ImageCheck.Evaluate(ImageReference.Parse("app:1.2.3")!, null, null, Lookup(new RegistryTag("1.2.3"), new RegistryTag("2024-01-01"), new RegistryTag("latest"), new RegistryTag("2.0")));

        check.Status.Should().Be(ImageStatus.UpToDate, "2.0 has two numbers and 1.2.3 has three: a different scheme");
    }

    [Fact]
    public void should_say_an_image_pinned_by_digest_is_pinned_not_floating()
    {
        var image = ImageReference.Parse("ghcr.io/me/app@sha256:" + new string('a', 64))!;

        ImageCheck.Evaluate(image, null, null, Lookup(new RegistryTag("1.0"))).Should().Match<ImageCheck>(c => c.Status == ImageStatus.Floating && c.Reason == ImageCheck.Pinned);
    }

    [Fact]
    public void should_call_a_floating_tag_floating_when_nothing_tells_its_version_or_digest()
    {
        var check = ImageCheck.Evaluate(ImageReference.Parse("nginx:latest")!, null, null, Lookup(new RegistryTag("latest")));

        check.Should().Match<ImageCheck>(c => c.Status == ImageStatus.Floating && c.Reason == ImageCheck.FloatingTag);
    }

    [Fact]
    public void should_not_compare_by_digest_when_the_registry_gave_none_for_the_tag()
    {
        var check = ImageCheck.Evaluate(ImageReference.Parse("nginx:latest")!, null, "sha256:running", Lookup(new RegistryTag("latest")));

        check.Status.Should().Be(ImageStatus.Floating);
    }
}
