using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker.Test;

internal static class DockerEnvironments
{
    /// <summary>An environment that declares this Docker endpoint, as the connector leaves it (D-105).</summary>
    public static CatalogEnvironment With(string id, string name, DockerEndpoint endpoint)
    {
        return new CatalogEnvironment(id, name, Connectors: new Dictionary<string, object> { [DockerEnvironmentConnector.SectionName] = endpoint }, ConnectorRuntime: RuntimeNames.Docker);
    }
}
