using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

/// <summary>The checks the catalog applies to a value, shared with the connectors that read their own sections (D-105).</summary>
public sealed class CatalogValuesFixture
{
    [Theory]
    [InlineData("http://10.0.0.10:9898", "http://10.0.0.10:9898/")]
    [InlineData("https://tool.homelab.example/path", "https://tool.homelab.example/path")]
    [InlineData("  http://10.0.0.10:9898  ", "http://10.0.0.10:9898/")]
    [InlineData("ftp://10.0.0.10", null)]
    [InlineData("10.0.0.10:9898", null)]
    [InlineData("/relative", null)]
    [InlineData("http://user:pass@10.0.0.10:9898", null)]
    [InlineData("http://user@10.0.0.10:9898", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_accept_an_http_address_without_credentials(string? value, string? expected)
    {
        CatalogValues.HttpUrl(value)?.ToString().Should().Be(expected);
        (CatalogValues.HttpUrl(value) is null).Should().Be(expected is null);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  value ", "value")]
    public void should_trim_a_value_and_treat_a_blank_one_as_missing(string? value, string? expected)
    {
        CatalogValues.Blank(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("TOKEN", true)]
    [InlineData("_TOKEN_2", true)]
    [InlineData("PORTARR_READER_SERVER_1", true)]
    [InlineData("2TOKEN", false)]
    [InlineData("pa ss", false)]
    [InlineData("a-b", false)]
    [InlineData("", false)]
    public void should_recognise_the_name_of_an_environment_variable(string value, bool expected)
    {
        CatalogValues.IsVariableName(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("24h", 86_400)]
    [InlineData("7d", 604_800)]
    [InlineData(" 2m ", 120)]
    [InlineData("0", 0)]
    [InlineData("1x", null)]
    [InlineData("1.5h", null)]
    [InlineData("-5m", null)]
    [InlineData("h", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_read_one_grammar_of_durations_in_every_unit(string? value, int? seconds)
    {
        CatalogValues.ParseDuration(value, TimeSpan.Zero, TimeSpan.FromDays(30))?.TotalSeconds.Should().Be(seconds);
        (CatalogValues.ParseDuration(value, TimeSpan.Zero, TimeSpan.FromDays(30)) is null).Should().Be(seconds is null);
    }

    [Fact]
    public void should_refuse_a_duration_outside_the_bounds_of_its_key()
    {
        CatalogValues.ParseDuration("59s", TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)).Should().BeNull();
        CatalogValues.ParseDuration("1d", TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)).Should().Be(TimeSpan.FromDays(1));
        CatalogValues.ParseDuration("25h", TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)).Should().BeNull();
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("5s", 5)]
    [InlineData(" 30s ", 30)]
    [InlineData("2m", 120)]
    [InlineData("1", 1)]
    [InlineData("5m", 300)]
    [InlineData("0s", null)]
    [InlineData("301", null)]
    [InlineData("6m", null)]
    [InlineData("1h", null)]
    [InlineData("soon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_read_an_interval_between_one_second_and_five_minutes(string? value, int? seconds)
    {
        var interval = CatalogValues.ParseDuration(value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));

        interval?.TotalSeconds.Should().Be(seconds);
        (interval is null).Should().Be(seconds is null);
    }

    [Fact]
    public void should_limit_a_variable_name_to_128_characters()
    {
        CatalogValues.IsVariableName(new string('A', 128)).Should().BeTrue();
        CatalogValues.IsVariableName(new string('A', 129)).Should().BeFalse();
    }
}
