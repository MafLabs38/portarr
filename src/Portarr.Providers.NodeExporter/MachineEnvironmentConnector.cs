using Portarr.Core.Catalog;

namespace Portarr.Providers.NodeExporter;

/// <summary>The settings of a <c>runtime: machine</c> environment: only the kind the dashboard shows, the machine is read by its exporters.</summary>
public sealed record MachineEndpoint(string Kind = MachineSource.MachineKind);

/// <summary>
/// <c>runtime: machine</c> declares a host that runs no container engine Portarr reads: a hypervisor (Proxmox, a bare KVM host), a
/// router, a NAS. It is one machine with no workload; what Portarr shows of it comes from the auxiliary sections of the
/// environment (<c>hostMetrics</c> for its CPU, memory and system, <c>nvidiaGpu</c> for its card), and from the VMs and containers
/// that name it as their <c>host</c> (D-59, D-126).
/// </summary>
public sealed class MachineEnvironmentConnector : IEnvironmentConnector
{
    public const string SectionName = "machine";

    public string Section => SectionName;

    public bool IsRuntime => true;

    public bool SingleMachine => true;

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context)
    {
        // `runtime: machine` is the whole declaration (the parser then reads an empty section): a `machine:` section has nothing to
        // say, so any key in it is an error, and one with no value declares nothing.
        return context.Read<MachineDocument>() is null ? null : new EnvironmentConnectorResult(new MachineEndpoint());
    }
}

/// <summary>The <c>machine:</c> section: nothing to configure. The empty document makes the reader refuse any key.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "A machine has no setting; the empty document is how an unknown key is refused.")]
internal sealed class MachineDocument
{
}

public static class MachineEnvironmentExtensions
{
    /// <summary>Whether this environment is a machine read by its exporters only.</summary>
    public static bool IsMachineOnly(this CatalogEnvironment environment)
    {
        return environment.Settings<MachineEndpoint>(MachineEnvironmentConnector.SectionName) is not null;
    }
}
