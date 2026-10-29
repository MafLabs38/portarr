using Portarr.Core.Configuration;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Configuration;

public sealed class PortarrOptionsValidatorFixture : CoreTest<PortarrOptionsValidator>
{
    [Fact]
    public void should_accept_sqlite_defaults()
    {
        var result = Subject.Validate(null, new PortarrOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_postgres_has_no_connection_string()
    {
        var options = new PortarrOptions { Database = new DatabaseOptions { Type = DatabaseType.Postgres } };

        var result = Subject.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PORTARR__DATABASE__CONNECTIONSTRING");
    }

    [Fact]
    public void should_fail_when_data_folder_is_empty()
    {
        var result = Subject.Validate(null, new PortarrOptions { DataFolder = " " });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PORTARR__DATAFOLDER");
    }

    [Theory]
    [InlineData("10.0.0.2, 10.0.1.0/24,fd00::/8", true)]
    [InlineData("10.0.0.2,proxy.homelab.example", false)]
    public void should_accept_only_ips_and_cidr_networks_as_trusted_proxies(string proxies, bool valid)
    {
        var result = Subject.Validate(null, new PortarrOptions { TrustedProxies = proxies });

        result.Succeeded.Should().Be(valid, result.FailureMessage);
    }
}
