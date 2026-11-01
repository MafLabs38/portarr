using Portarr.Core.Catalog;

namespace Portarr.Providers.NodeExporter.Test;

internal static class NodeExporterEnvironments
{
    /// <summary>A Docker host that declares this node_exporter, as the connector leaves it (D-105).</summary>
    public static CatalogEnvironment With(HostMetricsEndpoint endpoint, string id = "server-1")
    {
        return new CatalogEnvironment(
            id,
            "Server 1",
            Connectors: new Dictionary<string, object> { ["docker"] = new object(), [NodeExporterEnvironmentConnector.SectionName] = endpoint },
            ConnectorRuntime: RuntimeNames.Docker);
    }
}
