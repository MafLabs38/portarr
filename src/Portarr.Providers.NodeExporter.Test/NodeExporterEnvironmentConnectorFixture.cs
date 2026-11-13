using NSubstitute;
using Portarr.Core.Catalog;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>The <c>hostMetrics:</c> section of an environment (D-72, D-105).</summary>
public sealed class NodeExporterEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new NodeExporterEnvironmentConnector();

    // The section is only valid next to a runtime: a Docker stand-in is declared with it.
    protected override string? OtherKeys => "docker: { url: \"http://10.0.0.10:2375\" }";

    protected override IReadOnlyList<IEnvironmentConnector> Companions { get; } = [new StubEnvironmentConnector("docker", isRuntime: true)];

    protected override string ValidSection => "{ nodeExporter: \"http://10.0.0.10:9100/metrics\" }";

    protected override string? SectionWithCredentialsInUrl => "{ nodeExporter: \"http://user:pass@10.0.0.10:9100/metrics\" }";

    // The section carries no secret variable.
    protected override string? SectionWithLiteralSecret => null;

    protected override string? SectionWithSecretOutsideNamespace => null;

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

    [Fact]
    public void should_refuse_the_token_variable_of_the_retired_reader_and_name_the_key()
    {
        var result = Parse("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" }, hostMetrics: { nodeExporter: \"http://10.0.0.10:9100/metrics\", tokenEnv: PORTARR_READER_SERVER_1 } }\n");

        result.Errors.Should().NotBeEmpty().And.Contain(error => error.Contains("tokenEnv", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("5s", 5)]
    [InlineData("1m", 60)]
    [InlineData("30", 30)]
    public void should_read_the_agent_less_often_when_asked_to(string interval, int seconds)
    {
        var parsed = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, hostMetrics: {{ nodeExporter: \"http://10.0.0.10:9100/metrics\", interval: {interval} }} }}\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().HostMetrics()!.Interval.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("6m")]
    [InlineData("1h")]
    [InlineData("soon")]
    public void should_refuse_an_interval_that_is_not_one_second_to_five_minutes(string interval)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, docker: {{ url: \"http://10.0.0.10:2375\" }}, hostMetrics: {{ nodeExporter: \"http://10.0.0.10:9100/metrics\", interval: {interval} }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("hostMetrics.interval must be a duration");
    }

    [Fact]
    public void should_resolve_its_target_with_the_interval()
    {
        var environment = NodeExporterEnvironments.With(new HostMetricsEndpoint(new Uri("http://10.0.0.10:9100/metrics"), Interval: TimeSpan.FromSeconds(10)));

        var factory = Substitute.For<IHttpClientFactory>();
        var source = new NodeExporterHostMetricsSource(factory);

        source.TargetsOf(environment, null).Should().ContainSingle().Which.Interval.Should().Be(TimeSpan.FromSeconds(10));
    }
}
