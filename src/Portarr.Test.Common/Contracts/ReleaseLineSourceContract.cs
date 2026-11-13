using AwesomeAssertions;
using Portarr.Core.Platforms;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>A release line connector wired to a service that lists release lines: the connector, the platform it must name, how to make the service fail.</summary>
public sealed record ReleaseLineHarness(IReleaseLineSource Source, string Platform, Action Fail);

/// <summary>
/// The contract of the release lines capability (<see cref="IReleaseLineSource"/>, D-119), run against every connector that declares
/// it: it names its platform, lists each release line once with the newest version of that line, and throws when the service
/// cannot be read (never an empty list that would read as "no release exists"). A connector joins by inheriting this class (rule 8).
/// </summary>
public abstract class ReleaseLineSourceContract
{
    /// <summary>Wires the connector to a service that lists several release lines.</summary>
    protected abstract ReleaseLineHarness Arrange();

    [Fact]
    public void should_name_the_platform_it_lists()
    {
        var harness = Arrange();

        harness.Source.Platform.Should().Be(harness.Platform);
    }

    [Fact]
    public async Task should_list_each_release_line_once_with_its_newest_version()
    {
        var harness = Arrange();

        var lines = await harness.Source.ReadAsync(TestContext.Current.CancellationToken);

        lines.Should().NotBeEmpty();
        lines.Select(l => l.Line).Should().OnlyHaveUniqueItems().And.OnlyContain(line => !string.IsNullOrWhiteSpace(line));
        lines.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l.Latest));
    }

    [Fact]
    public async Task should_throw_when_the_service_cannot_be_read()
    {
        var harness = Arrange();
        harness.Fail();

        var act = () => harness.Source.ReadAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
    }
}
