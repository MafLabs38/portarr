using Portarr.Core.Metrics;

namespace Portarr.Core.Test.Metrics;

/// <summary>What a machine or an exporter says about itself is cleaned before it is kept.</summary>
public sealed class ReportedTextFixture
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\u0007\u001b[31m", "[31m")]
    [InlineData("  Ubuntu 25.10  ", "Ubuntu 25.10")]
    [InlineData("Ubuntu\n25.10", "Ubuntu25.10")]
    public void should_trim_and_strip_control_characters(string? text, string? expected)
    {
        ReportedText.Clean(text).Should().Be(expected);
    }

    [Fact]
    public void should_cut_a_text_longer_than_any_real_name()
    {
        ReportedText.Clean(new string('x', 10_000))!.Length.Should().Be(ReportedText.MaxLength);
    }
}
