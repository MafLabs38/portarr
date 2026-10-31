namespace Portarr.Core.Catalog;

/// <summary>
/// How a machine is reached (D-84): its SSH route and its web consoles. Descriptive only: Portarr never connects
/// with it. <c>Key</c> names a key file (<c>id_ed25519</c>, <c>~/.ssh/server-1</c>), never its content; the
/// parser rejects anything that looks like a secret.
/// </summary>
public sealed record MachineAccess(SshAccess? Ssh, IReadOnlyList<AccessConsole> Consoles, string? Notes);

/// <summary>An SSH route: <c>ssh -p Port User@Host</c>, through <c>Jump</c> when set (a host alias or name).</summary>
public sealed record SshAccess(string? Host, int? Port, string? User, string? Key, string? Jump, string? Alias);

/// <summary>A web console (hypervisor UI, cloud manager, router).</summary>
public sealed record AccessConsole(string Label, Uri Href);

/// <summary>
/// What the operator knows about one machine of an environment (a cluster node, a VPS): free Markdown notes and
/// how to reach it (D-84). Single-machine environments carry their notes and access themselves.
/// </summary>
public sealed record MachineKnowledge(string Name, string? Notes, MachineAccess? Access);

/// <summary>The kinds of network links between environments or machines (D-84).</summary>
public static class LinkKinds
{
    public static readonly IReadOnlyList<string> All = ["wireguard-tunnel", "vpn", "lan", "peering", "ssh-tunnel", "other"];
}

/// <summary>
/// A network link between two environments or machines (D-84): a WireGuard tunnel, a VPN, a shared LAN, a
/// peering. <c>From</c> and <c>To</c> are environment ids or machine names (<c>machines</c> of an environment,
/// <c>kubernetes.nodeHosts</c>), or <c>environment/machine</c>. <c>Network</c> is its address range, when it has one.
/// </summary>
public sealed record CatalogLink(string Id, string From, string To, string Kind, string? Name, string? Network, string? Notes);

/// <summary>The kinds of external providers (D-84).</summary>
public static class ProviderKinds
{
    public static readonly IReadOnlyList<string> All = ["cdn", "paas", "dns", "registrar", "cloud", "vps", "storage", "backup", "email", "git", "registry", "monitoring", "other"];
}

/// <summary>
/// An external provider or account the operator uses (D-84): a CDN, a DNS host, a registrar, a VPS host, an
/// object store. <c>Console</c> is its management page; <c>Account</c> a hint of which account (a name, never a
/// credential); <c>UsedBy</c> the apps and environments that depend on it.
/// </summary>
public sealed record CatalogProvider(
    string Id,
    string Name,
    string Kind,
    Uri? Console,
    string? Account,
    IReadOnlyList<string> UsedBy,
    string? Notes);
