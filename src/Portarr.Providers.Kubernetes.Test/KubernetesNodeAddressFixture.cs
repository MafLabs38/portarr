namespace Portarr.Providers.Kubernetes.Test;

/// <summary>The node addresses Portarr agrees to read a node_exporter at (D-72): an IP that is not itself and not a link-local neighbour.</summary>
public sealed class KubernetesNodeAddressFixture
{
    [Theory]
    [InlineData("10.0.0.21")]
    [InlineData("fd00::21")]
    public void should_accept_a_routable_ip(string address)
    {
        KubernetesNodeTargets.IsReachableAddress(address).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("::127.0.0.1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("ff02::1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void should_refuse_a_loopback_link_local_multicast_or_unspecified_ip(string address)
    {
        KubernetesNodeTargets.IsReachableAddress(address).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("node-1.lan")]
    [InlineData("not an address")]
    public void should_refuse_anything_that_is_not_an_ip_literal(string address)
    {
        KubernetesNodeTargets.IsReachableAddress(address).Should().BeFalse();
    }
}
