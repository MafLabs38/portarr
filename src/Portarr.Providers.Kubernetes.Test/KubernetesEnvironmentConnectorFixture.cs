using Portarr.Core.Catalog;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>The <c>kubernetes:</c> section of an environment (D-55, D-72, D-103, D-105): what the connector accepts and refuses.</summary>
public sealed class KubernetesEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new KubernetesEnvironmentConnector();

    protected override string ValidSection => "{ server: \"https://10.0.0.10:6443\", tokenEnv: PORTARR_K8S_CLUSTER }";

    protected override string? SectionWithCredentialsInUrl => "{ server: \"https://admin:secret@10.0.0.10:6443\" }";

    protected override string? SectionWithLiteralSecret => "{ server: \"https://10.0.0.10:6443\", tokenEnv: \"eyJhbGciOi.token\" }";

    // The ServiceAccount token variable is not confined to a Portarr namespace yet (issue #45).
    protected override string? SectionWithSecretOutsideNamespace => "{ server: \"https://attacker.example:6443\", tokenEnv: GITHUB_TOKEN }";


    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new KubernetesEnvironmentConnector(), new StubEnvironmentConnector("docker", isRuntime: true), new StubEnvironmentConnector("cloudflare", isRuntime: true, singleMachine: false)]);

    private static KubernetesCluster Cluster(string yaml)
    {
        var parsed = Parse(yaml);
        parsed.Errors.Should().BeEmpty();
        return parsed.Catalog.Environments[0].Kubernetes()!;
    }

    [Fact]
    public void should_read_a_cluster_with_a_token_variable_and_a_ca_file()
    {
        var parsed = Parse("environments:\n  - id: cluster\n    runtime: kubernetes\n    kubernetes: { server: \"https://10.0.0.10:6443\", tokenEnv: PORTARR_K8S_CLUSTER, caFile: cluster-ca.crt }\n");

        parsed.Errors.Should().BeEmpty();
        var environment = parsed.Catalog.Environments.Single();
        environment.Kubernetes().Should().Be(new KubernetesCluster(new Uri("https://10.0.0.10:6443"), "PORTARR_K8S_CLUSTER", "cluster-ca.crt"));
        environment.Runtime.Should().Be(RuntimeNames.Kubernetes);
        environment.SingleMachine.Should().BeFalse();
    }

    [Fact]
    public void should_accept_a_plain_http_api_server_only_when_asked_for_explicitly()
    {
        var cluster = Cluster("environments: [{ id: cluster, kubernetes: { server: \"http://10.0.0.10:8080\", allowInsecure: true } }]");

        cluster.Server.Should().Be(new Uri("http://10.0.0.10:8080"));
        cluster.AllowInsecure.Should().BeTrue();
    }

    [Fact]
    public void should_accept_allow_insecure_on_an_https_server_unchanged()
    {
        var cluster = Cluster("environments: [{ id: cluster, kubernetes: { server: \"https://10.0.0.10:6443\", allowInsecure: true } }]");

        cluster.Server.Should().Be(new Uri("https://10.0.0.10:6443"));
        cluster.AllowInsecure.Should().BeTrue();
    }

    [Fact]
    public void should_default_the_token_variable_and_infer_the_runtime()
    {
        var cluster = Cluster("environments: [{ id: cluster, kubernetes: { server: \"https://10.0.0.10:6443\" } }]");

        cluster.TokenVariable.Should().Be("K8S_TOKEN");
        cluster.CaFile.Should().BeNull();
        cluster.AllowInsecure.Should().BeFalse();
    }

    [Fact]
    public void should_keep_the_default_variable_and_let_the_operator_allow_another_name()
    {
        const string Section = "environments:\n  - id: cluster\n    runtime: kubernetes\n    kubernetes: { server: \"https://10.0.0.10:6443\", tokenEnv: OLD_CLUSTER_TOKEN }\n";
        var connectors = new IEnvironmentConnector[] { new KubernetesEnvironmentConnector() };

        CatalogParser.Parse(Section, connectors).Errors.Should().ContainSingle().Which.Should().Contain("PORTARR_K8S_");
        CatalogParser.Parse(Section, connectors, new HashSet<string> { "OLD_CLUSTER_TOKEN" }).Errors.Should().BeEmpty();
        CatalogParser.Parse(Section.Replace("OLD_CLUSTER_TOKEN", "K8S_TOKEN", StringComparison.Ordinal), connectors).Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("runtime: kubernetes", "kubernetes.server must be the API server URL")]
    [InlineData("kubernetes: { server: \"https://admin:secret@10.0.0.10:6443\" }", "without credentials")]
    [InlineData("kubernetes: { server: \"http://10.0.0.10:8080\" }", "must be https")]
    [InlineData("kubernetes: { server: \"https://10.0.0.10:6443\", tokenEnv: \"eyJhbGciOi.token\" }", "never the token itself")]
    [InlineData("kubernetes: { server: \"https://10.0.0.10:6443\", caFile: \" \" }", "kubernetes.caFile")]
    [InlineData("runtime: docker\n    kubernetes: { server: \"https://10.0.0.10:6443\" }", "a docker environment cannot declare kubernetes")]
    [InlineData("docker: { url: \"http://p:2375\" }\n    kubernetes: { server: \"https://10.0.0.10:6443\" }", "declare one connector")]
    public void should_reject_an_invalid_cluster(string body, string error)
    {
        var result = Parse($"environments:\n  - id: cluster\n    {body}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_default_the_node_exporter_pod_lookup_to_monitoring_and_its_app_label()
    {
        var nodeExporter = Cluster("environments: [{ id: cluster, kubernetes: { server: \"https://10.0.0.10:6443\", nodeExporter: { access: api-server } } }]").NodeExporter!;

        nodeExporter.Access.Should().Be(NodeExporterAccess.ApiServer);
        nodeExporter.Namespace.Should().Be("monitoring");
        nodeExporter.Selector.Should().Be("app.kubernetes.io/name=node-exporter");
    }

    [Fact]
    public void should_read_the_node_exporter_namespace_selector_and_routes()
    {
        var nodeExporter = Cluster("""
            environments: [{ id: cluster, kubernetes: { server: "https://10.0.0.10:6443", nodeExporter: { namespace: ops, selector: "app=exporter", nodes: { a: api-server, b: "http://10.0.0.30:9100/metrics", c: off } } } }]
            """).NodeExporter!;

        nodeExporter.Namespace.Should().Be("ops");
        nodeExporter.Selector.Should().Be("app=exporter");
        nodeExporter.RouteOf("a").Should().Be(new NodeExporterRoute(NodeExporterAccess.ApiServer));
        nodeExporter.RouteOf("b").Should().Be(new NodeExporterRoute(NodeExporterAccess.Url, new Uri("http://10.0.0.30:9100/metrics")));
        nodeExporter.RouteOf("c").Should().Be(new NodeExporterRoute(NodeExporterAccess.Off));
        nodeExporter.RouteOf("other").Should().Be(new NodeExporterRoute(NodeExporterAccess.Direct), "a node with no route follows the default access");
    }

    [Theory]
    [InlineData("namespace: Not_A_Namespace")]
    [InlineData("namespace: \" \"")]
    [InlineData("selector: \" \"")]
    public void should_reject_an_invalid_node_exporter_namespace_or_selector(string body)
    {
        var result = Parse($"environments: [{{ id: cluster, kubernetes: {{ server: \"https://10.0.0.10:6443\", nodeExporter: {{ {body} }} }} }}]");

        result.Errors.Should().ContainSingle().Which.Should().Contain("kubernetes.nodeExporter.namespace");
    }

    [Theory]
    [InlineData("5s", 5)]
    [InlineData("1m", 60)]
    public void should_read_the_nodes_less_often_when_asked_to(string interval, int seconds)
    {
        var nodeExporter = Cluster($"environments: [{{ id: cluster, kubernetes: {{ server: \"https://10.0.0.10:6443\", nodeExporter: {{ interval: {interval} }} }} }}]").NodeExporter!;

        nodeExporter.Interval.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("6m")]
    [InlineData("soon")]
    public void should_refuse_a_node_exporter_interval_that_is_not_one_second_to_five_minutes(string interval)
    {
        var result = Parse($"environments: [{{ id: cluster, kubernetes: {{ server: \"https://10.0.0.10:6443\", nodeExporter: {{ interval: {interval} }} }} }}]");

        result.Errors.Should().ContainSingle().Which.Should().Contain("kubernetes.nodeExporter.interval must be a duration");
    }

    [Theory]
    [InlineData("port: 0", "kubernetes.nodeExporter needs a port")]
    [InlineData("access: url", "kubernetes.nodeExporter needs a port")]
    [InlineData("nodes: { a: nowhere }", "kubernetes.nodeExporter.nodes maps a node name")]
    public void should_reject_an_invalid_node_exporter(string body, string error)
    {
        var result = Parse($"environments: [{{ id: cluster, kubernetes: {{ server: \"https://10.0.0.10:6443\", nodeExporter: {{ {body} }} }} }}]");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_give_the_host_of_its_nodes_like_any_other_host()
    {
        var parsed = Parse("""
            environments:
              - { id: hypervisor, docker: { url: "http://10.0.0.10:2375" } }
              - id: cluster
                kubernetes: { server: "https://10.0.0.20:6443", nodeHosts: { node-1: hypervisor } }
            """);

        parsed.Errors.Should().BeEmpty();
        var cluster = parsed.Catalog.Environments[1];
        cluster.HostOf("node-1").Should().Be("hypervisor");
        cluster.HostOf("node-2").Should().BeNull("a VPS whose hypervisor is not in the catalog has no host");
    }

    [Theory]
    [InlineData("nodeHosts: { node-1: nowhere }", "host 'nowhere'")]
    [InlineData("nodeHosts: { node-1: \"\" }", "kubernetes.nodeHosts maps node names")]
    public void should_refuse_a_node_host_that_is_not_an_environment(string body, string error)
    {
        var result = Parse($"environments:\n  - {{ id: vm-1, kubernetes: {{ server: \"https://10.0.0.20:6443\", {body} }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_not_be_a_machine_that_can_be_a_host()
    {
        var result = Parse("environments:\n  - { id: cluster, kubernetes: { server: \"https://10.0.0.20:6443\" } }\n  - { id: vm-1, host: cluster }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("must be a single machine");
    }
}
