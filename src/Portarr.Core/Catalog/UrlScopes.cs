using System.Net;
using System.Net.Sockets;

namespace Portarr.Core.Catalog;

public static class UrlScopes
{
    /// <summary>
    /// A URL on a private address (RFC 1918, loopback, link-local, IPv6 ULA) or a local-only
    /// name (localhost, *.local, *.lan, *.home.arpa) is a LAN URL; anything else is public.
    /// </summary>
    public static UrlScope Infer(Uri url)
    {
        return IsLanHost(url.Host) ? UrlScope.Lan : UrlScope.Public;
    }

    public static bool IsLanHost(string host)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return IsPrivate(address);
        }

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || (address.IsIPv4MappedToIPv6 && IsPrivate(address.MapToIPv4()));
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }
}
