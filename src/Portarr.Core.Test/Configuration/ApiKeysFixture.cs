using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Configuration;

/// <summary>The shared API keys (V5, D-70): read from either header, compared without leaking their content or length.</summary>
public sealed class ApiKeysFixture
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(Key, true)]
    [InlineData(Key + "x", false)]
    [InlineData("0123456789abcdef0123456789abcdeF", false)]
    [InlineData("", false)]
    public void should_accept_only_the_exact_key(string presented, bool accepted)
    {
        ApiKeys.Matches(presented, Key).Should().Be(accepted);
    }

    [Fact]
    public void should_ignore_the_whitespace_around_the_configured_key()
    {
        ApiKeys.Matches(Key, $"  {Key}\n").Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void should_refuse_everything_when_no_key_is_set(string? expected, string? presented)
    {
        ApiKeys.Matches(Key, expected).Should().BeFalse();
        ApiKeys.Matches(presented, Key).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc", null, "abc")]
    [InlineData(null, "Bearer abc", "abc")]
    [InlineData(null, "bearer abc", "abc")]
    [InlineData(null, "BEARER   abc  ", "abc")]
    [InlineData("", "Bearer abc", "abc")]
    [InlineData("abc", "Bearer other", "abc")]
    [InlineData(null, "Basic abc", null)]
    [InlineData(null, "Bearerabc", null)]
    [InlineData(null, "", null)]
    [InlineData("", "", null)]
    [InlineData(null, null, null)]
    public void should_read_the_key_from_x_api_key_first_else_a_bearer_token(string? header, string? authorization, string? expected)
    {
        ApiKeys.Read(header, authorization).Should().Be(expected);
    }
}
