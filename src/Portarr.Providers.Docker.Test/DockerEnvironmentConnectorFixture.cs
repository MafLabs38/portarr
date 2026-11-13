using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Docker.Test;

/// <summary>The <c>docker:</c> section of an environment (D-105).</summary>
public sealed class DockerEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new DockerEnvironmentConnector();

    protected override string ValidSection => "{ url: \"http://10.0.0.10:2375\" }";

    // The Docker address is not checked for credentials today: tightening it is a decision of its own.
    protected override string? SectionWithCredentialsInUrl => null;

    // The section carries no secret variable.
    protected override string? SectionWithLiteralSecret => null;

    protected override string? SectionWithSecretOutsideNamespace => null;

    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new DockerEnvironmentConnector()]);

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("tcp://10.0.0.10:2375")]
    [InlineData("http://socket-proxy:2375")]
    [InlineData("https://docker.homelab.example")]
    public void should_read_the_address_of_a_docker_engine(string url)
    {
        var parsed = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"{url}\" }} }}\n");

        parsed.Errors.Should().BeEmpty();
        var environment = parsed.Catalog.Environments.Single();
        environment.Docker().Should().Be(new DockerEndpoint(new Uri(url)));
        environment.Runtime.Should().Be("docker");
    }

    [Theory]
    [InlineData("ssh://host")]
    [InlineData("ssh://root:secret@host")]
    [InlineData("/var/run/docker.sock")]
    [InlineData("")]
    public void should_refuse_an_address_that_is_not_a_docker_engine_without_echoing_it(string url)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"{url}\" }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("docker.url must be").And.NotContain("secret");
    }

    [Fact]
    public void should_be_one_machine_that_can_host_vms()
    {
        var parsed = Parse("environments:\n  - { id: hypervisor, docker: { url: \"http://10.0.0.10:2375\" } }\n  - { id: vm-1, host: hypervisor }\n");

        parsed.Errors.Should().BeEmpty();
    }
}
