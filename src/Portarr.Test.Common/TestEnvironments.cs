using Portarr.Core.Catalog;

namespace Portarr.Test.Common;

/// <summary>Environments as the connector libraries leave them (D-105), for Core tests that only need the section declared.</summary>
public static class TestEnvironments
{
    /// <summary>A Docker host: it declares <c>docker</c>, which is its runtime, whatever the Docker library read from it.</summary>
    public static CatalogEnvironment DockerHost(string id, string name, string? address = null, string? host = null)
    {
        return new CatalogEnvironment(id, name, address, Host: host, Connectors: new Dictionary<string, object> { ["docker"] = new object() }, ConnectorRuntime: RuntimeNames.Docker);
    }

    /// <summary>A cluster: it declares <c>kubernetes</c>, which is its runtime, and says on which environment some of its nodes are VMs.</summary>
    public static CatalogEnvironment Cluster(string id, string name, IReadOnlyDictionary<string, string>? nodeHosts = null)
    {
        return new CatalogEnvironment(id, name, Connectors: new Dictionary<string, object> { ["kubernetes"] = new object() }, MachineHosts: nodeHosts, ConnectorRuntime: RuntimeNames.Kubernetes, SingleMachine: false);
    }
}
