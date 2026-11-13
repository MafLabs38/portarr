using AwesomeAssertions;
using Portarr.Core.Platforms;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>A lifecycle connector wired to a service that knows one product: the connector, a product it knows, one it does not, how to make the service fail.</summary>
public sealed record SupportLifecycleHarness(ISupportLifecycleSource Source, string KnownProduct, string UnknownProduct, Action Fail);

/// <summary>
/// The contract of the support lifecycle capability (<see cref="ISupportLifecycleSource"/>, D-119), run against every connector that
/// declares it: the cycles of a product it knows, once each; nothing (null) for a product it does not know or for text that is not
/// a product identifier; and a throw when the service cannot be read. A connector joins by inheriting this class (rule 8).
/// </summary>
public abstract class SupportLifecycleSourceContract
{
    /// <summary>Wires the connector to a service that knows a product and not another.</summary>
    protected abstract SupportLifecycleHarness Arrange();

    [Fact]
    public async Task should_list_the_cycles_of_a_known_product_once_each()
    {
        var harness = Arrange();

        var cycles = await harness.Source.ReadAsync(harness.KnownProduct, TestContext.Current.CancellationToken);

        cycles.Should().NotBeNullOrEmpty();
        cycles!.Select(c => c.Cycle).Should().OnlyHaveUniqueItems().And.OnlyContain(cycle => !string.IsNullOrWhiteSpace(cycle));
    }

    [Fact]
    public async Task should_know_nothing_of_an_unknown_product()
    {
        var harness = Arrange();

        (await harness.Source.ReadAsync(harness.UnknownProduct, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("Ubuntu")]
    [InlineData("a b")]
    [InlineData("")]
    public async Task should_not_look_up_text_that_is_not_a_product_identifier(string product)
    {
        var harness = Arrange();

        (await harness.Source.ReadAsync(product, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_the_service_cannot_be_read()
    {
        var harness = Arrange();
        harness.Fail();

        var act = () => harness.Source.ReadAsync(harness.KnownProduct, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
    }
}
