using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>
/// The machine runtime against the infrastructure contract (D-54, D-126). Its workload capability lists nothing by design (a host
/// without an engine), so the workload contract, which needs a workload to find, does not apply to it.
/// </summary>
public sealed class MachineInfrastructureSourceContractFixture : InfrastructureSourceContract
{
    protected override InfrastructureSourceHarness Arrange()
    {
        var machine = new CatalogEnvironment("server-1", "Server 1", null, Connectors: new Dictionary<string, object> { ["machine"] = new MachineEndpoint() }, ConnectorRuntime: "machine");
        return new InfrastructureSourceHarness(new MachineSource(), machine, new CatalogEnvironment("server-2", "Server 2"), []);
    }
}
