namespace Portarr.Mcp.Test;

/// <summary>The MCP endpoint's shared key (V5, D-70): off by default, a strong key when one is set.</summary>
public sealed class McpOptionsFixture
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, null, true)]
    [InlineData(true, "too-short", false)]
    [InlineData(false, "too-short", false)]
    [InlineData(true, Key, true)]
    [InlineData(true, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public void should_require_a_strong_key_only_when_one_is_set(bool enabled, string? key, bool valid)
    {
        var result = new McpOptionsValidator().Validate(null, new McpOptions { Enabled = enabled, ApiKey = key });

        result.Succeeded.Should().Be(valid);
    }
}
