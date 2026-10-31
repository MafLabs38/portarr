using Portarr.Core.Infrastructure;

namespace Portarr.Core.Test.Infrastructure;

/// <summary>The node addresses Portarr agrees to read a node_exporter at (D-72): never itself, never a link-local neighbour.</summary>
public sealed class HostMetricsAddressFixture
{
    [Theory]
    [InlineData("10.0.0.21")]
    [InlineData("192.168.1.10")]
    [InlineData("fd00::21")]
    [InlineData("node-1.lan")]
    public void should_accept_a_routable_address_or_a_host_name(string address)
    {
        HostMetricsMonitor.IsReachableAddress(address).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.1.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("localhost")]
    [InlineData("not an address")]
    public void should_skip_a_loopback_link_local_unspecified_or_malformed_address(string? address)
    {
        HostMetricsMonitor.IsReachableAddress(address).Should().BeFalse();
    }
}
