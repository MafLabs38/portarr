namespace Portarr.Core.Configuration;

/// <summary>
/// One of the shared keys of the environment (<c>PORTARR__APIKEY</c>, <c>PORTARR__MCP__APIKEY</c>), kept working as
/// bootstrap keys next to the accounts (V7, D-97): whoever presents one is the built-in <c>system</c> principal, with
/// the permissions its source declares (the view permissions, plus <c>catalog.write</c> where it is for writes; never
/// users, settings or secrets). Deprecated: per-user keys replace them.
/// </summary>
public interface ISystemApiKeySource
{
    /// <summary>The variable that sets it, for the startup warning.</summary>
    string Name { get; }

    /// <summary>The key, or null when the variable is not set.</summary>
    string? Key { get; }

    /// <summary>What the key may do: it never holds more than its purpose needs.</summary>
    IReadOnlySet<string> Permissions { get; }
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

    /// <summary>Reads everything and writes the catalog (what the key was introduced for); no chat, settings, users or secrets.</summary>
    public IReadOnlySet<string> Permissions { get; } = Accounts.Permissions.View.Append(Accounts.Permissions.CatalogWrite).ToHashSet(StringComparer.Ordinal);
}
