using System.Text.Json;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes.Test;

public sealed class KubernetesMapperFixture
{
    [Fact]
    public void should_map_a_ready_pod_with_its_namespace_node_labels_and_restarts()
    {
        var pod = Pod("""
            { "metadata": { "name": "api-7c4b-fghij", "namespace": "shop", "labels": { "app": "api" }, "creationTimestamp": "2026-10-30T08:00:00Z" },
              "spec": { "nodeName": "node-1", "containers": [{ "name": "api", "image": "registry.example/api:2" }] },
              "status": { "phase": "Running", "conditions": [{ "type": "Ready", "status": "True" }],
                          "containerStatuses": [{ "name": "api", "ready": true, "started": true, "restartCount": 3, "state": { "running": {} },
                                                 "imageID": "docker-pullable://registry.example/api@sha256:0123abcd" }] } }
            """);

        var workload = KubernetesMapper.ToWorkload("cluster", pod);

        workload.Id.Should().Be("shop/api-7c4b-fghij");
        workload.ImageDigest.Should().Be("sha256:0123abcd", "the running digest tells whether a floating tag moved (V4)");
        workload.Name.Should().Be("api-7c4b-fghij");
        workload.Namespace.Should().Be("shop");
        workload.Node.Should().Be("node-1");
        workload.Image.Should().Be("registry.example/api:2");
        workload.State.Should().Be(WorkloadState.Running);
        workload.Health.Should().Be(WorkloadHealth.Healthy);
        workload.StatusText.Should().Be("Running · 1/1 ready · 3 restarts");
        workload.Labels.Should().Contain("app", "api");
        workload.CreatedAt.Should().Be(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("Running", "\"ready\": false, \"started\": true, \"restartCount\": 0, \"state\": { \"running\": {} }", "False", WorkloadState.Running, WorkloadHealth.Unhealthy, "Running · 0/1 ready")]
    [InlineData("Running", "\"ready\": false, \"started\": false, \"restartCount\": 0, \"state\": { \"running\": {} }", "False", WorkloadState.Running, WorkloadHealth.Starting, "Running · 0/1 ready")]
    [InlineData("Running", "\"ready\": false, \"restartCount\": 12, \"state\": { \"waiting\": { \"reason\": \"CrashLoopBackOff\" } }", "False", WorkloadState.Restarting, WorkloadHealth.None, "CrashLoopBackOff · 12 restarts")]
    [InlineData("Pending", "\"ready\": false, \"restartCount\": 0, \"state\": { \"waiting\": { \"reason\": \"ContainerCreating\" } }", "False", WorkloadState.Created, WorkloadHealth.None, "Pending (ContainerCreating)")]
    [InlineData("Pending", "\"ready\": false, \"restartCount\": 0, \"state\": { \"waiting\": { \"reason\": \"ImagePullBackOff\" } }", "False", WorkloadState.Restarting, WorkloadHealth.None, "ImagePullBackOff")]
    [InlineData("Succeeded", "\"ready\": false, \"restartCount\": 0, \"state\": { \"terminated\": { \"reason\": \"Completed\", \"exitCode\": 0 } }", "False", WorkloadState.Exited, WorkloadHealth.None, "Completed")]
    [InlineData("Failed", "\"ready\": false, \"restartCount\": 1, \"state\": { \"terminated\": { \"reason\": \"Error\", \"exitCode\": 1 } }", "False", WorkloadState.Dead, WorkloadHealth.None, "Failed (Error) · 1 restart")]
    public void should_derive_state_health_and_text_from_phase_and_container_statuses(string phase, string container, string ready, WorkloadState state, WorkloadHealth health, string text)
    {
        var pod = Pod($$"""
            { "metadata": { "name": "p", "namespace": "n" },
              "status": { "phase": "{{phase}}", "conditions": [{ "type": "Ready", "status": "{{ready}}" }], "containerStatuses": [{ "name": "c", {{container}} }] } }
            """);

        var workload = KubernetesMapper.ToWorkload("cluster", pod);

        workload.State.Should().Be(state);
        workload.Health.Should().Be(health);
        workload.StatusText.Should().Be(text);
    }

    [Fact]
    public void should_show_a_terminating_pod_as_removing()
    {
        var pod = Pod("""{ "metadata": { "name": "p", "namespace": "n", "deletionTimestamp": "2026-10-30T08:00:00Z" }, "status": { "phase": "Running" } }""");

        KubernetesMapper.ToWorkload("cluster", pod).State.Should().Be(WorkloadState.Removing);
    }

    [Fact]
    public void should_map_a_node_with_its_capacity_roles_readiness_and_usage()
    {
        var node = JsonSerializer.Deserialize("""
            { "metadata": { "name": "node-1", "labels": { "node-role.kubernetes.io/control-plane": "true", "node-role.kubernetes.io/etcd": "true" } },
              "status": { "capacity": { "cpu": "4", "memory": "20481188Ki" }, "conditions": [{ "type": "Ready", "status": "True" }] } }
            """, KubernetesJsonContext.Default.Node)!;
        var metrics = new NodeMetrics(node.Metadata, new Dictionary<string, string> { ["cpu"] = "40122367n", ["memory"] = "1238720Ki" });

        var machine = KubernetesMapper.ToMachine(node, metrics);

        machine.Should().Be(new MachineSample("node-1", "node-1", MachineKind.KubernetesNode, 4, 20_972_736_512, 0.04, 1_268_449_280, UsageScope.Host, machine.Roles, true));
        machine.Roles.Should().Equal("control-plane", "etcd");
        KubernetesMapper.ToMachine(node, null).CpuCores.Should().BeNull();
    }

    [Fact]
    public void should_sum_the_containers_of_a_pod()
    {
        var metrics = JsonSerializer.Deserialize("""
            { "metadata": { "name": "vault-0", "namespace": "secrets" },
              "containers": [{ "name": "vault", "usage": { "cpu": "3m", "memory": "100Mi" } }, { "name": "sidecar", "usage": { "cpu": "2m", "memory": "28Mi" } }] }
            """, KubernetesJsonContext.Default.PodMetrics)!;

        KubernetesMapper.ToUsage(metrics).Should().Be(new WorkloadUsage(0.005, 134_217_728, null));
    }

    private static Pod Pod(string json) => JsonSerializer.Deserialize(json, KubernetesJsonContext.Default.Pod)!;
}
