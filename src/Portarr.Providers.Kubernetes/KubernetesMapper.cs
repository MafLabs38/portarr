using System.Globalization;
using System.Text.RegularExpressions;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes;

/// <summary>Pods as workloads, nodes as machines (D-55).</summary>
internal static partial class KubernetesMapper
{
    /// <summary>What a node is called on the dashboard.</summary>
    public const string NodeKind = "kubernetes-node";

    private const string NodeRolePrefix = "node-role.kubernetes.io/";

    /// <summary>Waiting reasons of a container that keeps failing rather than starting.</summary>
    private static readonly HashSet<string> FailingReasons = new(StringComparer.Ordinal)
    {
        "CrashLoopBackOff", "ImagePullBackOff", "ErrImagePull", "CreateContainerConfigError", "CreateContainerError", "InvalidImageName",
    };

    /// <summary>
    /// A pod. Id <c>namespace/name</c>; state from its phase and container statuses; health from readiness
    /// (a running pod that is not ready fails its readiness probe, unless a container is still starting).
    /// </summary>
    public static Workload ToWorkload(string environmentId, Pod pod)
    {
        var metadata = pod.Metadata;
        var containers = pod.Status?.ContainerStatuses ?? [];
        var state = ParseState(pod);
        var restarts = containers.Sum(c => c.RestartCount);

        return new Workload(
            environmentId,
            $"{metadata.Namespace}/{metadata.Name}",
            metadata.Name,
            pod.Spec?.Containers?.FirstOrDefault()?.Image ?? string.Empty,
            state,
            state == WorkloadState.Running ? ParseHealth(pod) : WorkloadHealth.None,
            StatusText(pod, state, restarts),
            metadata.CreationTimestamp ?? DateTimeOffset.UnixEpoch,
            null,
            metadata.Labels ?? new Dictionary<string, string>(),
            null,
            metadata.Namespace,
            pod.Spec?.NodeName,
            ToMounts(pod.Spec),
            ImageDigest(pod));
    }

    /// <summary>
    /// The registry digest of the first container's running image, from its status (<c>docker.io/library/postgres@sha256:…</c>,
    /// sometimes prefixed with <c>docker-pullable://</c>); null while the image is pulled or for a locally built image.
    /// </summary>
    public static string? ImageDigest(Pod pod)
    {
        var name = pod.Spec?.Containers?.FirstOrDefault()?.Name;
        var imageId = pod.Status?.ContainerStatuses?.FirstOrDefault(c => c.Name == name)?.ImageId;
        var at = imageId?.LastIndexOf("@sha256:", StringComparison.Ordinal) ?? -1;
        return at < 0 ? null : imageId![(at + 1)..];
    }

    /// <summary>
    /// The pod's persistent storage: the claims and host paths its containers mount, once each (two
    /// containers often mount the same claim). Config maps, secrets and emptyDir hold no data to back up.
    /// </summary>
    public static IReadOnlyList<WorkloadMount> ToMounts(PodSpec? spec)
    {
        var volumes = (spec?.Volumes ?? []).ToDictionary(v => v.Name, StringComparer.Ordinal);
        return (spec?.Containers ?? [])
            .SelectMany(container => container.VolumeMounts ?? [])
            .Select(mount => (Mount: mount, Volume: volumes.GetValueOrDefault(mount.Name)))
            .Select(pair => pair.Volume switch
            {
                { PersistentVolumeClaim: { } claim } => new WorkloadMount(MountKind.Claim, claim.ClaimName, pair.Mount.MountPath),
                { HostPath: { } hostPath } => new WorkloadMount(MountKind.HostPath, hostPath.Path, pair.Mount.MountPath),
                _ => null,
            })
            .OfType<WorkloadMount>()
            .DistinctBy(mount => (mount.Kind, mount.Source))
            .ToList();
    }

    /// <summary>
    /// The size requested by each claim a StatefulSet creates (<c>&lt;template&gt;-&lt;set&gt;-&lt;ordinal&gt;</c>),
    /// by <c>namespace/claim</c>. Claims of other workloads are not readable without access to
    /// PersistentVolumeClaims, which the read-only ServiceAccount does not have.
    /// </summary>
    public static Dictionary<string, long> ToClaimCapacities(StatefulSetList sets)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var set in sets.Items)
        {
            var replicas = Math.Clamp(set.Spec?.Replicas ?? 1, 1, 100);
            foreach (var template in set.Spec?.VolumeClaimTemplates ?? [])
            {
                var bytes = KubernetesQuantity.Bytes(template.Spec?.Resources?.Requests, "storage");
                for (var ordinal = 0; bytes is not null && ordinal < replicas; ordinal++)
                {
                    result[$"{set.Metadata.Namespace}/{template.Metadata.Name}-{set.Metadata.Name}-{ordinal}"] = bytes.Value;
                }
            }
        }

        return result;
    }

    public static WorkloadState ParseState(Pod pod)
    {
        if (pod.Metadata.DeletionTimestamp is not null)
        {
            return WorkloadState.Removing;
        }

        var failing = (pod.Status?.ContainerStatuses ?? []).Any(c => c.State?.Waiting?.Reason is { } reason && FailingReasons.Contains(reason));

        return pod.Status?.Phase switch
        {
            "Running" or "Pending" when failing => WorkloadState.Restarting,
            "Running" => WorkloadState.Running,
            "Pending" => WorkloadState.Created,
            "Succeeded" => WorkloadState.Exited,
            "Failed" => WorkloadState.Dead,
            _ => WorkloadState.Unknown,
        };
    }

    public static WorkloadHealth ParseHealth(Pod pod)
    {
        var ready = pod.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready")?.Status;
        if (ready == "True")
        {
            return WorkloadHealth.Healthy;
        }

        var containers = pod.Status?.ContainerStatuses ?? [];
        return containers.Any(c => c.Started == false) ? WorkloadHealth.Starting : WorkloadHealth.Unhealthy;
    }

    /// <summary>"Running · 2/2 ready · 3 restarts", "Pending (ContainerCreating)", "CrashLoopBackOff · 12 restarts", "Completed".</summary>
    public static string StatusText(Pod pod, WorkloadState state, int restarts)
    {
        var containers = pod.Status?.ContainerStatuses ?? [];
        var reason = containers.Select(c => c.State?.Waiting?.Reason ?? c.State?.Terminated?.Reason).FirstOrDefault(r => r is not null)
            ?? pod.Status?.Reason;

        var text = state switch
        {
            WorkloadState.Running => $"Running · {containers.Count(c => c.Ready)}/{containers.Count} ready",
            WorkloadState.Restarting => reason ?? "Restarting",
            WorkloadState.Removing => "Terminating",
            WorkloadState.Exited => reason ?? "Completed",
            _ when reason is not null => $"{pod.Status?.Phase ?? "Unknown"} ({reason})",
            _ => pod.Status?.Phase ?? "Unknown",
        };

        return restarts switch
        {
            0 => text,
            1 => $"{text} · 1 restart",
            _ => $"{text} · {restarts} restarts",
        };
    }

    /// <summary>A node: capacity from its status, usage from metrics.k8s.io when it is available, its InternalIP (where a node_exporter answers, D-72).</summary>
    public static MachineSample ToMachine(Node node, NodeMetrics? metrics)
    {
        var labels = node.Metadata.Labels ?? new Dictionary<string, string>();
        var roles = labels.Keys
            .Where(key => key.StartsWith(NodeRolePrefix, StringComparison.Ordinal) && key.Length > NodeRolePrefix.Length)
            .Select(key => key[NodeRolePrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToList();
        var ready = node.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready")?.Status;

        return new MachineSample(
            node.Metadata.Name,
            node.Metadata.Name,
            NodeKind,
            KubernetesQuantity.Cores(node.Status?.Capacity),
            KubernetesQuantity.Bytes(node.Status?.Capacity),
            KubernetesQuantity.Cores(metrics?.Usage),
            KubernetesQuantity.Bytes(metrics?.Usage),
            UsageScope.Host,
            roles,
            ready is null ? null : ready == "True",
            metrics?.Timestamp,
            node.Status?.Addresses?.FirstOrDefault(a => a.Type == "InternalIP")?.Address);
    }

    /// <summary>A metrics window ("20.064s", "1m0s") in time; null when it cannot be read.</summary>
    public static TimeSpan? ToWindow(string? window)
    {
        if (string.IsNullOrEmpty(window))
        {
            return null;
        }

        var match = DurationPattern().Match(window);
        if (!match.Success || match.Length != window.Length)
        {
            return null;
        }

        var minutes = match.Groups["m"].Success ? double.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = match.Groups["s"].Success ? double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromSeconds((minutes * 60) + seconds);
    }

    [GeneratedRegex(@"^(?:(?<m>\d+)m)?(?:(?<s>\d+(?:\.\d+)?)s)?$")]
    private static partial Regex DurationPattern();

    /// <summary>A pod's usage: the sum of its containers.</summary>
    public static WorkloadUsage ToUsage(PodMetrics metrics)
    {
        var containers = metrics.Containers ?? [];
        var cores = containers.Sum(c => KubernetesQuantity.Cores(c.Usage) ?? 0);
        var bytes = containers.Sum(c => KubernetesQuantity.Bytes(c.Usage) ?? 0);

        return new WorkloadUsage(Math.Round(cores, 3), bytes, null);
    }
}
