using Microsoft.AspNetCore.Http;
using Portarr.Core.Configuration;
using Portarr.Http.Authentication;

namespace Portarr.Mcp.Test;

/// <summary>The MCP endpoint's key (V5, D-70): off by default, a strong key when on, read from either header.</summary>
public sealed class ApiKeyFixture
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
    public void should_refuse_everything_when_no_key_is_set()
    {
        ApiKeys.Matches(Key, null).Should().BeFalse();
    }

    [Theory]
    [InlineData("X-Api-Key", "abc", "abc")]
    [InlineData("Authorization", "Bearer abc", "abc")]
    [InlineData("Authorization", "Basic abc", null)]
    public void should_read_the_key_from_x_api_key_or_a_bearer_token(string header, string value, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = value;

        ApiKeyAuthenticationHandler.ReadKey(context.Request).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, null, true)]
    [InlineData(true, "too-short", false)]
    [InlineData(false, "too-short", false)]
    [InlineData(true, Key, true)]
    public void should_require_a_strong_key_only_when_one_is_set(bool enabled, string? key, bool valid)
    {
        var result = new McpOptionsValidator().Validate(null, new McpOptions { Enabled = enabled, ApiKey = key });

        result.Succeeded.Should().Be(valid);
    }
}
