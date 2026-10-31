using Microsoft.Extensions.Options;

namespace Portarr.Mcp;

/// <summary>
/// The MCP server (V5, D-70), section <c>Mcp</c>: <c>PORTARR__MCP__ENABLED</c>, <c>PORTARR__MCP__APIKEY</c>.
/// Off by default. Until Portarr has accounts (C2), the endpoint requires this key in an <c>X-Api-Key</c> or
/// <c>Authorization: Bearer</c> header.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Shortest key accepted: 32 characters (e.g. <c>openssl rand -hex 32</c> gives 64).</summary>
    public const int MinimumKeyLength = Portarr.Core.Configuration.ApiKeys.MinimumLength;

    public bool Enabled { get; set; }

    /// <summary>The key MCP clients send. Never returned by the API, never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The catalog write tools (D-86), off by default: <c>PORTARR__MCP__WRITETOOLS</c>. They also need
    /// <c>PORTARR__CATALOG__WRITABLE</c>; until then they answer that the catalog is read-only.
    /// </summary>
    public bool WriteTools { get; set; }
}

/// <summary>Fails startup when the endpoint is on without a strong enough key.</summary>
public sealed class McpOptionsValidator : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        if (options.Enabled && (options.ApiKey is null || options.ApiKey.Trim().Length < McpOptions.MinimumKeyLength))
        {
            return ValidateOptionsResult.Fail(
                $"Mcp:ApiKey must be at least {McpOptions.MinimumKeyLength} characters when Mcp:Enabled is true (PORTARR__MCP__APIKEY, e.g. openssl rand -hex 32).");
        }

        return ValidateOptionsResult.Success;
    }
}
