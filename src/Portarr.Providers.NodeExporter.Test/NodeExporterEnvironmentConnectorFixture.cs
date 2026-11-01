using Portarr.Core.Catalog;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>The <c>hostMetrics:</c> section of an environment, and the <c>reader:</c> shortcut that stands for it (D-72, D-102, D-105).</summary>
public sealed class NodeExporterEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new NodeExporterEnvironmentConnector();

    // The section is only valid next to a runtime: a Docker stand-in is declared with it.
    protected override string? OtherKeys => "docker: { url: \"http://10.0.0.10:2375\" }";

    protected override IReadOnlyList<IEnvironmentConnector> Companions { get; } = [new StubEnvironmentConnector("docker", isRuntime: true)];

    protected override string ValidSection => "{ nodeExporter: \"http://10.0.0.10:9100/metrics\", tokenEnv: PORTARR_READER_SERVER_1 }";

    protected override string? SectionWithCredentialsInUrl => "{ nodeExporter: \"http://user:pass@10.0.0.10:9100/metrics\" }";

    protected override string? SectionWithLiteralSecret => "{ nodeExporter: \"http://10.0.0.10:9100/metrics\", tokenEnv: \"ghp_a literal secret\" }";

    protected override string? SectionWithSecretOutsideNamespace => "{ nodeExporter: \"http://attacker.example:9100/metrics\", tokenEnv: GITHUB_TOKEN }";

    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new NodeExporterEnvironmentConnector(), new StubEnvironmentConnector("docker", isRuntime: true), new StubEnvironmentConnector("cloudflare", isRuntime: true, singleMachine: false), new StandInKubernetesConnector()]);

    [Fact]
    public void should_read_the_agent_of_a_docker_host()
    {
        var parsed = Parse("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" }, hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().HostMetrics().Should().Be(new HostMetricsEndpoint(new Uri("http://10.0.0.10:9100/metrics")));
    }

    [Theory]
    [InlineData("environments:\n  - { id: cluster, kubernetes: { server: \"https://10.0.0.20:6443\" }, hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" } }\n")]
    [InlineData("environments:\n  - { id: cdn, cloudflare: { accountId: abc }, hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" } }\n")]
    [InlineData("environments:\n  - { id: bare, hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\" } }\n")]
    public void should_refuse_an_agent_on_anything_but_a_docker_host(string yaml)
    {
        var result = Parse(yaml);

        result.Errors.Should().ContainSingle().Which.Should().Contain("hostMetrics is for a docker environment");
    }

    [Theory]
    [InlineData("ftp://10.0.0.10:9100/metrics")]
    [InlineData("10.0.0.10:9100")]
    [InlineData("")]
    public void should_refuse_an_address_that_is_not_http(string url)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, hostMetrics: {{ nodeExporter: \"{url}\" }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("hostMetrics.nodeExporter must be node_exporter's metrics URL");
    }

    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("CLOUDFLARE_API_TOKEN")]
    [InlineData("PORTARR_READER_")]
    [InlineData("portarr_reader_lower")]
    public void should_refuse_to_send_any_other_variable_of_the_process_to_a_url(string variable)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, hostMetrics: {{ nodeExporter: \"http://attacker.example:9100/metrics\", tokenEnv: {variable} }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("hostMetrics.tokenEnv").And.Contain("PORTARR_READER_");
    }

    [Fact]
    public void should_read_a_reader_as_the_agent_it_stands_for()
    {
        var parsed = Parse("environments:\n  - { id: server-1, reader: { url: \"https://proxy.homelab.example/reader-1\", tokenEnv: PORTARR_READER_PROXY } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().HostMetrics().Should().Be(new HostMetricsEndpoint(new Uri("https://proxy.homelab.example/reader-1/v1/host"), "PORTARR_READER_PROXY"));
    }
}
