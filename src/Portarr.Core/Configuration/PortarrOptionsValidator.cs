using Microsoft.Extensions.Options;

namespace Portarr.Core.Configuration;

/// <summary>
/// Fails startup with a clear message instead of an obscure connection error later.
/// </summary>
public sealed class PortarrOptionsValidator : IValidateOptions<PortarrOptions>
{
    public ValidateOptionsResult Validate(string? name, PortarrOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DataFolder))
        {
            failures.Add("DataFolder must not be empty (PORTARR__DATAFOLDER).");
        }

        if (options.Database.Type == DatabaseType.Postgres && string.IsNullOrWhiteSpace(options.Database.ConnectionString))
        {
            failures.Add("Database:ConnectionString is required when Database:Type is Postgres (PORTARR__DATABASE__CONNECTIONSTRING).");
        }

        foreach (var entry in options.TrustedProxyEntries.Where(entry => !PortarrOptions.TryParseProxy(entry, out _)))
        {
            failures.Add($"TrustedProxies entry '{entry}' must be an IP address or a CIDR network such as 10.0.0.0/24 (PORTARR__TRUSTEDPROXIES).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
