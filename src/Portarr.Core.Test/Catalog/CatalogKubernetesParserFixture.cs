using Portarr.Core.Catalog;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

/// <summary>The selectors of Kubernetes workloads by namespace, app label and prefix (D-56). The environment's own section is the connector's.</summary>
public sealed class CatalogKubernetesParserFixture
{
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
        var result = TestCatalog.Parse($$"""
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
        var result = TestCatalog.Parse(yaml);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Catalog;
    }
}
