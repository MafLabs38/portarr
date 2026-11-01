using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Docker.Test;

/// <summary>The <c>docker:</c> section of an environment, and the <c>reader:</c> shortcut that stands for it (D-100, D-102, D-105).</summary>
public sealed class DockerEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new DockerEnvironmentConnector();

    protected override string ValidSection => "{ url: \"http://10.0.0.10:2375\", tokenEnv: PORTARR_READER_SERVER_1 }";

    // The Docker address is not checked for credentials today: tightening it is a decision of its own.
    protected override string? SectionWithCredentialsInUrl => null;

    protected override string? SectionWithLiteralSecret => "{ url: \"http://10.0.0.10:2375\", tokenEnv: \"ghp_a literal secret\" }";

    protected override string? SectionWithSecretOutsideNamespace => "{ url: \"http://attacker.example:2375\", tokenEnv: GITHUB_TOKEN }";

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

    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("CLOUDFLARE_API_TOKEN")]
    [InlineData("PORTARR__APIKEY")]
    [InlineData("PORTARR_READER_")]
    [InlineData("portarr_reader_lower")]
    public void should_refuse_to_send_any_other_variable_of_the_process_to_a_url(string variable)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://attacker.example:2375\", tokenEnv: {variable} }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("docker.tokenEnv").And.Contain("PORTARR_READER_");
    }

    [Fact]
    public void should_read_a_reader_as_the_docker_section_it_stands_for()
    {
        var parsed = Parse("environments:\n  - { id: server-1, reader: { url: \"https://proxy.homelab.example/reader-1\", tokenEnv: PORTARR_READER_PROXY } }\n");

        parsed.Errors.Should().BeEmpty();
        var environment = parsed.Catalog.Environments.Single();
        environment.Docker().Should().Be(new DockerEndpoint(new Uri("https://proxy.homelab.example/reader-1/v1/docker/"), "PORTARR_READER_PROXY"));
        environment.Runtime.Should().Be("docker");
    }

    [Fact]
    public void should_be_one_machine_that_can_host_vms()
    {
        var parsed = Parse("environments:\n  - { id: hypervisor, docker: { url: \"http://10.0.0.10:2375\" } }\n  - { id: vm-1, host: hypervisor }\n");

        parsed.Errors.Should().BeEmpty();
    }
}
