using System.Net;

namespace Portarr.Providers.Registries.Test;

/// <summary>Where Portarr may connect to read a registry: not itself, not a link-local metadata service; a LAN registry is fine (15-20).</summary>
public sealed class RegistryAddressPolicyFixture
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    [InlineData("fe80::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("224.0.0.1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void should_refuse_loopback_link_local_unspecified_and_multicast_addresses(string address)
    {
        RegistryAddressPolicy.IsAllowed(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.10")]
    [InlineData("192.168.1.20")]
    [InlineData("172.16.5.5")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("::ffff:10.0.0.10")]
    public void should_allow_a_lan_registry_and_public_addresses(string address)
    {
        RegistryAddressPolicy.IsAllowed(IPAddress.Parse(address)).Should().BeTrue();
    }

    [Fact]
    public async Task should_refuse_to_connect_to_an_address_literal_that_is_loopback()
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = RegistryAddressPolicy.ConnectAsync });

        var request = () => client.GetAsync("https://127.0.0.1:9/v2/", TestContext.Current.CancellationToken);

        (await request.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("may connect to");
    }
}
