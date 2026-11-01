using Microsoft.Extensions.Options;

namespace Portarr.Mcp;

/// <summary>
/// The MCP server (V5, D-70), section <c>Mcp</c>: <c>PORTARR__MCP__ENABLED</c>, <c>PORTARR__MCP__APIKEY</c>.
/// Off by default. The endpoint takes a per-user API key or the chat runner's token (V7, D-97, D-98) in an
/// <c>X-Api-Key</c> or <c>Authorization: Bearer</c> header; this shared key keeps working as a deprecated bootstrap
/// key, as the built-in <c>system</c> principal with the view permissions (and <c>catalog.write</c> while the write tools are on).
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Shortest key accepted: 32 characters (e.g. <c>openssl rand -hex 32</c> gives 64).</summary>
    public const int MinimumKeyLength = Portarr.Core.Configuration.ApiKeys.MinimumLength;

    public bool Enabled { get; set; }

    /// <summary>The deprecated shared key MCP clients send. Never returned by the API, never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The catalog write tools (D-86), off by default: <c>PORTARR__MCP__WRITETOOLS</c>. They also need
    /// <c>PORTARR__CATALOG__WRITABLE</c>; until then they answer that the catalog is read-only.
    /// </summary>
    public bool WriteTools { get; set; }
}

/// <summary><c>PORTARR__MCP__APIKEY</c> as a system key (deprecated, V7).</summary>
public sealed class McpApiKeySource : Portarr.Core.Configuration.ISystemApiKeySource
{
    private readonly IOptionsMonitor<McpOptions> _options;

    public McpApiKeySource(IOptionsMonitor<McpOptions> options)
    {
        _options = options;
    }

    public string Name => "PORTARR__MCP__APIKEY";

    public string? Key => _options.CurrentValue.ApiKey;

    /// <summary>The view permissions the MCP tools read with, and <c>catalog.write</c> only while the write tools are on.</summary>
    public IReadOnlySet<string> Permissions => _options.CurrentValue.WriteTools
        ? Portarr.Core.Accounts.Permissions.View.Append(Portarr.Core.Accounts.Permissions.CatalogWrite).ToHashSet(StringComparer.Ordinal)
        : Portarr.Core.Accounts.Permissions.View;
}

/// <summary>Fails startup when the deprecated shared key is set but too weak.</summary>
public sealed class McpOptionsValidator : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        // The shared key is optional now (per-user keys exist), but when set it must be strong.
        if (!string.IsNullOrWhiteSpace(options.ApiKey) && options.ApiKey.Trim().Length < McpOptions.MinimumKeyLength)
        {
            return ValidateOptionsResult.Fail(
                $"Mcp:ApiKey must be at least {McpOptions.MinimumKeyLength} characters when set (PORTARR__MCP__APIKEY, e.g. openssl rand -hex 32); it is deprecated: use per-user API keys.");
        }

        return ValidateOptionsResult.Success;
    }
}
