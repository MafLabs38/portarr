using AwesomeAssertions;
using Portarr.Core.Maintenance;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// An image registry connector wired to a registry that serves one repository: the connector under test, an image it knows (with
/// the tag to describe), an image whose repository does not exist, how to make the registry fail, and the methods it saw (null
/// when they cannot be observed).
/// </summary>
public sealed record ImageRegistryHarness(
    IImageRegistry Registry,
    ImageReference Known,
    string KnownTag,
    ImageReference Missing,
    Action Fail,
    Func<IReadOnlyList<HttpMethod>>? Methods = null);

/// <summary>
/// The contract of the image registry capability (<see cref="IImageRegistry"/>, D-66), run against every connector that declares
/// it: tags with unique names, a description of a tag that is that tag, nothing for a tag that does not exist, a reason for a
/// repository that does not exist or a registry that fails, and nothing pulled (GET and HEAD only). A connector joins by
/// inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class ImageRegistryContract
{
    /// <summary>Wires the connector to a registry that serves the repository of <see cref="ImageRegistryHarness.Known"/>.</summary>
    protected abstract ImageRegistryHarness Arrange();

    [Fact]
    public async Task should_list_the_tags_of_a_repository_with_unique_names()
    {
        var harness = Arrange();

        var tags = await harness.Registry.ListTagsAsync(harness.Known, TestContext.Current.CancellationToken);

        tags.Should().NotBeEmpty();
        tags.Select(t => t.Name).Should().OnlyHaveUniqueItems().And.OnlyContain(name => !string.IsNullOrWhiteSpace(name));
        tags.Select(t => t.Name).Should().Contain(harness.KnownTag);
    }

    [Fact]
    public async Task should_describe_a_tag_as_that_tag()
    {
        var harness = Arrange();

        var described = await harness.Registry.DescribeAsync(harness.Known, harness.KnownTag, TestContext.Current.CancellationToken);

        described.Should().NotBeNull();
        described!.Name.Should().Be(harness.KnownTag);
    }

    [Fact]
    public async Task should_say_nothing_of_a_tag_that_does_not_exist()
    {
        var harness = Arrange();

        var described = await harness.Registry.DescribeAsync(harness.Known, "no-such-tag-ever", TestContext.Current.CancellationToken);

        described.Should().BeNull();
    }

    [Fact]
    public async Task should_give_a_reason_for_a_repository_that_does_not_exist()
    {
        var harness = Arrange();

        var list = () => harness.Registry.ListTagsAsync(harness.Missing, TestContext.Current.CancellationToken);

        (await list.Should().ThrowAsync<RegistryUnavailableException>()).Which.Reason.Should().Be(RegistryUnavailableException.NotFound);
    }

    [Fact]
    public async Task should_fail_when_the_registry_cannot_be_read_not_answer_an_empty_list()
    {
        var harness = Arrange();
        harness.Fail();

        var list = () => harness.Registry.ListTagsAsync(harness.Known, TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_only_read_the_registry()
    {
        var harness = Arrange();
        if (harness.Methods is null)
        {
            return;
        }

        await harness.Registry.ListTagsAsync(harness.Known, TestContext.Current.CancellationToken);
        await harness.Registry.DescribeAsync(harness.Known, harness.KnownTag, TestContext.Current.CancellationToken);

        harness.Methods().Should().NotBeEmpty().And.OnlyContain(m => m == HttpMethod.Get || m == HttpMethod.Head, "nothing is pulled or pushed");
    }
}
