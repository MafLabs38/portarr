using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Portarr.Providers.Registries;

/// <summary>
/// Where Portarr may connect to read a registry (15-20). The registry host comes from the name of a running image, so
/// whoever can start a container could name any host: a loopback or link-local address (the cloud metadata service,
/// Portarr's own listener), or an address literal, must not become an HTTPS request from Portarr. The check is made on
/// the address actually connected to, after the name is resolved, so a name that resolves there is refused too, and it
/// covers the token realm and every redirect, which go through the same handler. A private registry on a LAN address
/// (RFC 1918) stays allowed: local registries are legitimate in a homelab.
/// </summary>
public static class RegistryAddressPolicy
{
    /// <summary>Whether an address may be connected to: not loopback, link-local, unspecified or multicast (IPv4-mapped IPv6 is unmapped first).</summary>
    public static bool IsAllowed(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal;
        }

        var bytes = address.GetAddressBytes();
        var linkLocal = bytes[0] == 169 && bytes[1] == 254;
        var multicast = bytes[0] >= 224;
        return !linkLocal && !multicast;
    }

    /// <summary>The handler's connect step: resolves the host, keeps the allowed addresses and connects to the first that answers.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)).Where(IsAllowed).ToList();
        if (addresses.Count == 0)
        {
            throw new HttpRequestException($"The registry host '{context.DnsEndPoint.Host}' resolves to no address Portarr may connect to.");
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException)
            {
                socket.Dispose();
                last = exception;
            }
        }

        throw new HttpRequestException($"The registry host '{context.DnsEndPoint.Host}' did not answer.", last);
    }
}
