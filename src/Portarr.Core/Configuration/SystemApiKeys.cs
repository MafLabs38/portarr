namespace Portarr.Core.Configuration;

/// <summary>
/// One of the shared keys of the environment (<c>PORTARR__APIKEY</c>, <c>PORTARR__MCP__APIKEY</c>), kept working as
/// bootstrap keys next to the accounts (V7, D-97): whoever presents one is the built-in <c>system</c> principal, with
/// the Admin role's permissions. Deprecated: per-user keys replace them.
/// </summary>
public interface ISystemApiKeySource
{
    /// <summary>The variable that sets it, for the startup warning.</summary>
    string Name { get; }

    /// <summary>The key, or null when the variable is not set.</summary>
    string? Key { get; }
}

/// <summary><c>PORTARR__APIKEY</c>, the key catalog writes asked for before accounts.</summary>
public sealed class ApiKeySource : ISystemApiKeySource
{
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<PortarrOptions> _options;

    public ApiKeySource(Microsoft.Extensions.Options.IOptionsMonitor<PortarrOptions> options)
    {
        _options = options;
    }

    public string Name => "PORTARR__APIKEY";

    public string? Key => _options.CurrentValue.ApiKey;
}
