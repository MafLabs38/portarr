using Portarr.Core.Catalog;

namespace Portarr.Providers.Cloudflare.Test;

internal static class CloudflareEnvironments
{
    /// <summary>An environment that declares this account, as the connector leaves it (D-105).</summary>
    public static CatalogEnvironment With(CloudflareAccount account, string id = "cdn")
    {
        return new CatalogEnvironment(id, "CDN", null, Connectors: Declared(account), ConnectorRuntime: CloudflareEnvironmentConnector.SectionName);
    }

    public static IReadOnlyDictionary<string, object> Declared(CloudflareAccount account)
    {
        return new Dictionary<string, object> { [CloudflareEnvironmentConnector.SectionName] = account };
    }
}
