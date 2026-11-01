using Portarr.Core.Catalog;

namespace Portarr.Providers.Cloudflare;

/// <summary>
/// A Cloudflare account (D-53). <c>TokenVariable</c> names the environment variable holding the API
/// token: the token itself is never written in the catalog.
/// </summary>
public sealed record CloudflareAccount(string AccountId, string TokenVariable);

/// <summary>The <c>cloudflare:</c> section of an environment (D-53).</summary>
internal sealed class CloudflareDocument
{
    public string? AccountId { get; set; }

    /// <summary>Name of the environment variable holding the API token (never the token itself).</summary>
    public string? TokenEnv { get; set; }
}

/// <summary>
/// <c>cloudflare:</c> declares a Cloudflare account as an environment (D-53, D-105). The account id is only required here: a
/// placeholder still loads, and the connector says it is not configured instead of calling Cloudflare.
/// </summary>
public sealed class CloudflareEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "cloudflare";

    public const string DefaultTokenVariable = "CLOUDFLARE_API_TOKEN";

    public string Section => SectionName;

    public bool IsRuntime => true;

    /// <summary>An account is not a machine: it cannot host a VM, carry a Backrest or a host agent.</summary>
    public bool SingleMachine => false;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        if (context.Read<CloudflareDocument>() is not { } document)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(document.AccountId))
        {
            context.Error("cloudflare.accountId is required (the id in the Cloudflare dashboard URL).");
            return null;
        }

        var variable = document.TokenEnv ?? DefaultTokenVariable;
        if (!CatalogValues.IsVariableName(variable))
        {
            context.Error($"cloudflare.tokenEnv must be the name of an environment variable (e.g. {DefaultTokenVariable}), never the token itself.");
            return null;
        }

        return new EnvironmentConnectorResult(new CloudflareAccount(document.AccountId.Trim(), variable));
    }
}

public static class CloudflareEnvironmentExtensions
{
    /// <summary>The Cloudflare account this environment declares, or null.</summary>
    public static CloudflareAccount? Cloudflare(this CatalogEnvironment environment)
    {
        return environment.Settings<CloudflareAccount>(CloudflareEnvironmentConnector.SectionName);
    }
}
