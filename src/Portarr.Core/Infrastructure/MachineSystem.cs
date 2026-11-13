namespace Portarr.Core.Infrastructure;

/// <summary>
/// What a machine says about its own system (D-118), as the tools Portarr reads report it: the operating system and the kernel
/// (node_exporter's <c>node_os_info</c> and <c>node_uname_info</c>, or a Kubernetes node's <c>nodeInfo</c>) and, on a cluster node,
/// the version of the kubelet (<c>v1.31.4+k3s1</c>). Every field is optional: null means the source did not say.
/// <c>OsId</c> is the identifier of <c>/etc/os-release</c> (<c>ubuntu</c>, <c>debian</c>), the key to look a product up by;
/// <c>OsName</c> is the text to show (<c>Ubuntu 25.10</c>); <c>OsVersion</c> is the version alone (<c>25.10</c>).
/// </summary>
public sealed record MachineSystem(string? OsId, string? OsName, string? OsVersion, string? Kernel, string? KubeletVersion = null)
{
    /// <summary>True when the source said nothing at all.</summary>
    public bool IsEmpty => OsId is null && OsName is null && OsVersion is null && Kernel is null && KubeletVersion is null;
}
