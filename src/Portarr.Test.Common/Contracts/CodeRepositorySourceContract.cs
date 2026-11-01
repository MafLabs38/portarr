using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Maintenance;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// A code host connector wired to a host that serves a source's repositories: the connector under test, a source it reads, one it
/// does not, how to make the host fail, and the methods it saw (null when they cannot be observed).
/// </summary>
public sealed record CodeRepositorySourceHarness(
    ICodeRepositorySource Source,
    ICodeSource Declared,
    ICodeSource Unsupported,
    Action Fail,
    Func<IReadOnlyList<HttpMethod>>? Methods = null);

/// <summary>
/// The contract of the code host capability (<see cref="ICodeRepositorySource"/>, D-64), run against every connector that declares
/// it (GitHub): it reads the sources of its own kind and no other, lists repositories with a name and an address, never writes
/// (D-16), says a host it cannot read instead of answering an empty list, and knows its own addresses and image names without
/// failing on anything else. A connector joins by inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class CodeRepositorySourceContract
{
    /// <summary>Wires the connector to a host that serves the repositories of <see cref="CodeRepositorySourceHarness.Declared"/>.</summary>
    protected abstract CodeRepositorySourceHarness Arrange();

    [Fact]
    public void should_read_the_kind_of_source_it_is_wired_to_and_no_other()
    {
        var harness = Arrange();

        harness.Source.Supports(harness.Declared).Should().BeTrue();
        harness.Source.Supports(harness.Unsupported).Should().BeFalse();
    }

    [Fact]
    public void should_be_ready_to_call_when_it_is_wired()
    {
        var harness = Arrange();

        harness.Source.GetConfigurationError(harness.Declared).Should().BeNull();
    }

    [Fact]
    public async Task should_list_repositories_with_a_unique_full_name_and_an_absolute_address()
    {
        var harness = Arrange();

        var repositories = await harness.Source.ScanAsync(harness.Declared, TestContext.Current.CancellationToken);

        repositories.Should().NotBeEmpty();
        repositories.Select(r => r.FullName).Should().OnlyHaveUniqueItems();
        repositories.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Owner) && !string.IsNullOrWhiteSpace(r.Name) && r.Url.IsAbsoluteUri);
    }

    [Fact]
    public async Task should_only_read_the_host()
    {
        var harness = Arrange();
        if (harness.Methods is null)
        {
            return;
        }

        await harness.Source.ScanAsync(harness.Declared, TestContext.Current.CancellationToken);

        harness.Methods().Should().NotBeEmpty().And.OnlyContain(m => m == HttpMethod.Get || m == HttpMethod.Head, "a connector never writes (D-16)");
    }

    [Fact]
    public async Task should_fail_when_the_host_cannot_be_read_not_answer_an_empty_list()
    {
        var harness = Arrange();
        harness.Fail();

        var scan = () => harness.Source.ScanAsync(harness.Declared, TestContext.Current.CancellationToken);

        await scan.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://elsewhere.invalid/owner/name")]
    public void should_not_claim_an_address_or_an_image_that_is_not_its_hosts_and_not_fail_on_them(string text)
    {
        var source = Arrange().Source;

        source.RepositoryOfSourceLabel(text).Should().BeNull();
        var image = ImageReference.Parse(text);
        if (image is not null)
        {
            source.RepositoryOfImage(image).Should().Match<string?>(name => name == null || name.Contains('/', StringComparison.Ordinal));
        }
    }
}
