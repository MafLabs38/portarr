using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>A GPU connector wired to a fake exporter: the environments it does and does not support, and what the exporter says or does.</summary>
public sealed record GpuHarness(IGpuSource Source, CatalogEnvironment Declared, CatalogEnvironment NotDeclared, Action ServeOneCard, Action ServeNothing, Action Fail);

/// <summary>
/// The contract of the GPU capability (<see cref="IGpuSource"/>, D-120), run against every connector that declares it: it supports the
/// environments that declare a card and no other, gives one reading per card with an identifier and a name, a share of a load only
/// between 0 and 100, nothing for an exporter that shows no card, and throws when the exporter cannot be read. A connector joins by
/// inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class GpuSourceContract
{
    /// <summary>Wires the connector to a fake exporter that serves one card.</summary>
    protected abstract GpuHarness Arrange();

    [Fact]
    public void should_support_the_environments_that_declare_a_card_and_no_other()
    {
        var harness = Arrange();

        harness.Source.Supports(harness.Declared).Should().BeTrue();
        harness.Source.Supports(harness.NotDeclared).Should().BeFalse();
    }

    [Fact]
    public async Task should_give_one_reading_per_card_with_an_identifier_a_name_and_loads_between_0_and_100()
    {
        var harness = Arrange();
        harness.ServeOneCard();

        var cards = await harness.Source.ReadAsync(harness.Declared, TestContext.Current.CancellationToken);

        var card = cards.Should().ContainSingle().Subject;
        card.Id.Should().NotBeNullOrWhiteSpace();
        card.Name.Should().NotBeNullOrWhiteSpace();
        new[] { card.UtilizationPercent, card.EncoderPercent, card.DecoderPercent }.Where(v => v is not null).Should().OnlyContain(v => v >= 0 && v <= 100);
        card.MemoryUsedBytes.Should().BeLessThanOrEqualTo(card.MemoryTotalBytes ?? long.MaxValue);
    }

    [Fact]
    public async Task should_give_nothing_for_an_exporter_that_shows_no_card()
    {
        var harness = Arrange();
        harness.ServeNothing();

        (await harness.Source.ReadAsync(harness.Declared, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_the_exporter_cannot_be_read()
    {
        var harness = Arrange();
        harness.Fail();

        var act = () => harness.Source.ReadAsync(harness.Declared, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
    }
}
