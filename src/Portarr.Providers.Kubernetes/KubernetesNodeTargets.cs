using System.Net;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

/// <summary>The route of a node whose exporter is reached through the API server's pod proxy, on this port (D-72).</summary>
internal sealed record PodProxyRoute(int Port);

/// <summary>Where each node of a cluster is read: its route to node_exporter, from the nodes the infrastructure monitor last saw (D-72).</summary>
internal static class KubernetesNodeTargets
{
    public static IEnumerable<HostMetricsTarget> Of(CatalogEnvironment environment, KubernetesNodeExporter nodeExporter, EnvironmentMetrics? infrastructure)
    {
        foreach (var node in (infrastructure?.Machines ?? []).Select(machine => machine.Machine))
        {
            if (NodeTarget(environment, nodeExporter, node) is { } target)
            {
                yield return target;
            }
        }
    }

    private static HostMetricsTarget? NodeTarget(CatalogEnvironment environment, KubernetesNodeExporter nodeExporter, MachineSample node)
    {
        var route = nodeExporter.RouteOf(node.Id);
        return route.Access switch
        {
            NodeExporterAccess.Url => new HostMetricsTarget(environment, node.Id, route.Url),
            NodeExporterAccess.ApiServer => new HostMetricsTarget(environment, node.Id, null, new PodProxyRoute(nodeExporter.Port)),
            NodeExporterAccess.Direct when node.Address is { } address
                => IsReachableAddress(address)
                    ? new HostMetricsTarget(environment, node.Id, new UriBuilder(Uri.UriSchemeHttp, IPAddress.Parse(address).ToString(), nodeExporter.Port, "/metrics").Uri)
                    : new HostMetricsTarget(environment, node.Id, null, null, ReasonCodes.InvalidAddress),
            _ => null,
        };
    }

    /// <summary>An address a node's exporter is read at: an IP that is not itself and not a link-local neighbour (a compromised node must not point Portarr at the metadata service).</summary>
    internal static bool IsReachableAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return false;
        }

        var bytes = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).GetAddressBytes();
        return bytes.Length == 4 ? IsRoutableIPv4(bytes) : IsRoutableIPv6(bytes);
    }

    private static bool IsRoutableIPv4(byte[] bytes)
    {
        return bytes[0] is not (0 or 127 or >= 224) && !(bytes[0] == 169 && bytes[1] == 254);
    }

    private static bool IsRoutableIPv6(byte[] bytes)
    {
        var embedded = bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0; // ::, ::1 and the IPv4-compatible ::a.b.c.d
        var nat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;
        var linkLocal = bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
        return !embedded && !nat64 && !linkLocal && bytes[0] != 0xff;
    }
}
