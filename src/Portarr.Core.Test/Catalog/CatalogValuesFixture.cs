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

    [Fact]
    public void should_limit_a_variable_name_to_128_characters()
    {
        CatalogValues.IsVariableName(new string('A', 128)).Should().BeTrue();
        CatalogValues.IsVariableName(new string('A', 129)).Should().BeFalse();
    }
}
