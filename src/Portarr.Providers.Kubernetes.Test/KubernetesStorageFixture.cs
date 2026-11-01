using System.Text.Json;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>Pod storage and StatefulSet claim sizes.</summary>
public sealed class KubernetesStorageFixture
{
    [Fact]
    public void should_keep_claims_and_host_paths_once_and_ignore_config_volumes()
    {
        var pod = Json("""
            { "metadata": { "name": "db-0", "namespace": "shop" },
              "spec": { "containers": [
                  { "name": "db", "image": "postgres:16", "volumeMounts": [{ "name": "data", "mountPath": "/var/lib/postgresql/data" }, { "name": "conf", "mountPath": "/etc/conf" }] },
                  { "name": "exporter", "image": "exporter:1", "volumeMounts": [{ "name": "data", "mountPath": "/data" }, { "name": "dumps", "mountPath": "/backup" }] }],
                "volumes": [{ "name": "data", "persistentVolumeClaim": { "claimName": "data-db-0" } }, { "name": "conf", "configMap": {} }, { "name": "dumps", "hostPath": { "path": "/mnt/backup" } }] } }
            """, KubernetesJsonContext.Default.Pod);

        KubernetesMapper.ToWorkload("cluster", pod).StorageMounts.Should().Equal(
            new WorkloadMount(MountKind.Claim, "data-db-0", "/var/lib/postgresql/data"),
            new WorkloadMount(MountKind.HostPath, "/mnt/backup", "/backup"));
    }

    [Fact]
    public void should_read_the_size_requested_by_each_claim_of_a_statefulset()
    {
        var sets = Json("""
            { "items": [{ "metadata": { "name": "db", "namespace": "shop" },
                          "spec": { "replicas": 2, "volumeClaimTemplates": [{ "metadata": { "name": "data" }, "spec": { "resources": { "requests": { "storage": "10Gi" } } } }] } }] }
            """, KubernetesJsonContext.Default.StatefulSetList);

        KubernetesMapper.ToClaimCapacities(sets).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["shop/data-db-0"] = 10L * 1024 * 1024 * 1024,
            ["shop/data-db-1"] = 10L * 1024 * 1024 * 1024,
        });
    }

    private static T Json<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) => JsonSerializer.Deserialize(json, type)!;
}
