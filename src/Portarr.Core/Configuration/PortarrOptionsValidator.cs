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

        if (options.Catalog.Writable && (options.ApiKey is null || options.ApiKey.Trim().Length < ApiKeys.MinimumLength))
        {
            failures.Add($"ApiKey must be at least {ApiKeys.MinimumLength} characters when Catalog:Writable is true (PORTARR__APIKEY, e.g. openssl rand -hex 32).");
        }

        if (options.Catalog.Backups is < 0 or > 1000)
        {
            failures.Add("Catalog:Backups must be between 0 and 1000 (PORTARR__CATALOG__BACKUPS).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
