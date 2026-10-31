using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Catalog;

/// <summary>Kubernetes environments (D-55) and their selectors by namespace, app label and prefix (D-56).</summary>
public sealed class CatalogKubernetesParserFixture
{
    [Fact]
    public void should_read_a_kubernetes_environment_with_a_token_variable_and_a_ca_file()
    {
        var catalog = Parse("""
            environments:
              - id: cluster
                runtime: kubernetes
                kubernetes: { server: "https://10.0.0.10:6443", tokenEnv: CLUSTER_TOKEN, caFile: cluster-ca.crt }
            """);

        catalog.Environments[0].Kubernetes.Should().Be(new KubernetesCluster(new Uri("https://10.0.0.10:6443"), "CLUSTER_TOKEN", "cluster-ca.crt"));
        catalog.Environments[0].Runtime.Should().Be(RuntimeNames.Kubernetes);
    }

    [Fact]
    public void should_accept_a_plain_http_api_server_only_when_asked_for_explicitly()
    {
        var cluster = Parse("environments: [{ id: cluster, kubernetes: { server: \"http://10.0.0.10:8080\", allowInsecure: true } }]").Environments[0].Kubernetes!;

        cluster.Server.Should().Be(new Uri("http://10.0.0.10:8080"));
        cluster.AllowInsecure.Should().BeTrue();
    }

    [Fact]
    public void should_accept_allow_insecure_on_an_https_server_unchanged()
    {
        var cluster = Parse("environments: [{ id: cluster, kubernetes: { server: \"https://10.0.0.10:6443\", allowInsecure: true } }]").Environments[0].Kubernetes!;

        cluster.Server.Should().Be(new Uri("https://10.0.0.10:6443"));
        cluster.AllowInsecure.Should().BeTrue();
    }

    [Fact]
    public void should_default_the_token_variable_and_infer_the_runtime()
    {
        var cluster = Parse("environments: [{ id: cluster, kubernetes: { server: \"https://10.0.0.10:6443\" } }]").Environments[0].Kubernetes!;

        cluster.TokenVariable.Should().Be("K8S_TOKEN");
        cluster.CaFile.Should().BeNull();
        cluster.AllowInsecure.Should().BeFalse();
    }

    [Theory]
    [InlineData("runtime: kubernetes", "kubernetes.server must be the API server URL")]
    [InlineData("kubernetes: { server: \"https://admin:secret@10.0.0.10:6443\" }", "without credentials")]
    [InlineData("kubernetes: { server: \"http://10.0.0.10:8080\" }", "must be https")]
    [InlineData("kubernetes: { server: \"https://10.0.0.10:6443\", tokenEnv: \"eyJhbGciOi.token\" }", "never the token itself")]
    [InlineData("kubernetes: { server: \"https://10.0.0.10:6443\", caFile: \" \" }", "kubernetes.caFile")]
    [InlineData("runtime: docker\n    kubernetes: { server: \"https://10.0.0.10:6443\" }", "a docker environment cannot declare kubernetes")]
    [InlineData("docker: { url: \"http://p:2375\" }\n    kubernetes: { server: \"https://10.0.0.10:6443\" }", "declare one connector")]
    public void should_reject_an_invalid_kubernetes_environment(string body, string error)
    {
        var result = CatalogParser.Parse($"environments:\n  - id: cluster\n    {body}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_default_the_node_exporter_pod_lookup_to_monitoring_and_its_app_label()
    {
        var nodeExporter = Parse("""
            environments: [{ id: cluster, kubernetes: { server: "https://10.0.0.10:6443", nodeExporter: { access: api-server } } }]
            """).Environments[0].Kubernetes!.NodeExporter!;

        nodeExporter.Access.Should().Be(NodeExporterAccess.ApiServer);
        nodeExporter.Namespace.Should().Be("monitoring");
        nodeExporter.Selector.Should().Be("app.kubernetes.io/name=node-exporter");
    }

    [Fact]
    public void should_read_the_node_exporter_namespace_and_selector()
    {
        var nodeExporter = Parse("""
            environments: [{ id: cluster, kubernetes: { server: "https://10.0.0.10:6443", nodeExporter: { namespace: ops, selector: "app=exporter" } } }]
            """).Environments[0].Kubernetes!.NodeExporter!;

        nodeExporter.Namespace.Should().Be("ops");
        nodeExporter.Selector.Should().Be("app=exporter");
    }

    [Theory]
    [InlineData("namespace: Not_A_Namespace")]
    [InlineData("namespace: \" \"")]
    [InlineData("selector: \" \"")]
    public void should_reject_an_invalid_node_exporter_namespace_or_selector(string body)
    {
        var result = CatalogParser.Parse($"environments: [{{ id: cluster, kubernetes: {{ server: \"https://10.0.0.10:6443\", nodeExporter: {{ {body} }} }} }}]");

        result.Errors.Should().ContainSingle().Which.Should().Contain("kubernetes.nodeExporter.namespace");
    }

    [Fact]
    public void should_read_selectors_by_namespace_app_label_and_prefix()
    {
        var selectors = Parse("""
            environments: [{ id: cluster, kubernetes: { server: "https://10.0.0.10:6443" } }]
            apps:
              - id: shop
                workloads:
                  - { runtime: kubernetes@cluster, match: { namespace: shop, app: shop-api } }
                  - { runtime: kubernetes@cluster, match: { namespace: shop, namePrefix: shop-worker- }, role: optional }
                  - { runtime: kubernetes@cluster, match: { name: shop-db-0 } }
            """).Apps[0].Workloads;

        selectors[0].Should().Be(new WorkloadSelector("cluster", null, Namespace: "shop", App: "shop-api"));
        selectors[1].Should().Be(new WorkloadSelector("cluster", null, MemberRole.Optional, Namespace: "shop", NamePrefix: "shop-worker-"));
        selectors[2].Should().Be(new WorkloadSelector("cluster", "shop-db-0"));
        selectors[0].Description.Should().Be("shop/app=shop-api");
        selectors[1].Description.Should().Be("shop/shop-worker-*");
    }

    [Theory]
    [InlineData("kubernetes@cluster", "{ namespace: shop }", "needs match.app")]
    [InlineData("docker@host", "{ name: web, app: web }", "only apply to kubernetes workloads")]
    public void should_reject_an_ambiguous_selector(string runtime, string match, string error)
    {
        var result = CatalogParser.Parse($$"""
            environments:
              - { id: cluster, kubernetes: { server: "https://10.0.0.10:6443" } }
              - { id: host, docker: { url: "http://p:2375" } }
            apps:
              - { id: shop, workloads: [{ runtime: {{runtime}}, match: {{match}} }] }
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_match_pods_by_label_across_rollouts_and_pick_the_running_ready_one()
    {
        var selector = new WorkloadSelector("cluster", null, Namespace: "shop", App: "shop-api");
        var old = Pod("shop-api-6d9f-abcde", "shop", "shop-api", WorkloadState.Running, WorkloadHealth.Unhealthy, 1);
        var current = Pod("shop-api-7c4b-fghij", "shop", "shop-api", WorkloadState.Running, WorkloadHealth.Healthy, 2);
        var other = Pod("shop-api-7c4b-zzzzz", "staging", "shop-api", WorkloadState.Running, WorkloadHealth.Healthy, 3);
        var unlabelled = Pod("shop-api-x", "shop", null, WorkloadState.Running, WorkloadHealth.Healthy, 4);

        selector.Pick([old, current, other, unlabelled]).Should().Be(current);
        selector.Matches(other).Should().BeFalse();
        selector.Matches(unlabelled).Should().BeFalse();
        new WorkloadSelector("cluster", null, NamePrefix: "shop-api-7c4b-").Pick([old, current]).Should().Be(current);
    }

    private static Workload Pod(string name, string ns, string? app, WorkloadState state, WorkloadHealth health, int minutes)
    {
        var labels = app is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["app"] = app };
        return new Workload("cluster", $"{ns}/{name}", name, "shop:1", state, health, "Running", DateTimeOffset.UnixEpoch.AddMinutes(minutes), null, labels, null, ns, "node-1");
    }

    private static Portarr.Core.Catalog.Catalog Parse(string yaml)
    {
        var result = CatalogParser.Parse(yaml);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Catalog;
    }
}
