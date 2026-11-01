using Portarr.Core.Catalog;

namespace Portarr.Providers.Kubernetes.Test;

internal static class KubernetesEnvironments
{
    /// <summary>An environment that declares this cluster, as the connector leaves it (D-105).</summary>
    public static CatalogEnvironment With(KubernetesCluster cluster, string id = "cluster")
    {
        return new CatalogEnvironment(
            id,
            "Cluster",
            Connectors: new Dictionary<string, object> { [KubernetesEnvironmentConnector.SectionName] = cluster },
            MachineHosts: cluster.NodeHosts,
            ConnectorRuntime: RuntimeNames.Kubernetes,
            SingleMachine: false);
    }

    /// <summary>The same environment with another cluster declared.</summary>
    public static CatalogEnvironment Replace(this CatalogEnvironment environment, KubernetesCluster cluster) => With(cluster, environment.Id);
}
