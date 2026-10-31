using System.Text.Json.Serialization;

namespace Portarr.Providers.Kubernetes;

// Subset of the Kubernetes API (core/v1, apps/v1 and metrics.k8s.io/v1beta1) responses Portarr reads.

internal sealed record ListMetadata([property: JsonPropertyName("continue")] string? Continue);

internal sealed record ObjectMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("namespace")] string? Namespace,
    [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels,
    [property: JsonPropertyName("creationTimestamp")] DateTimeOffset? CreationTimestamp,
    [property: JsonPropertyName("deletionTimestamp")] DateTimeOffset? DeletionTimestamp = null);

internal sealed record PodList(
    [property: JsonPropertyName("metadata")] ListMetadata? Metadata,
    [property: JsonPropertyName("items")] IReadOnlyList<Pod> Items);

internal sealed record Pod(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] PodSpec? Spec,
    [property: JsonPropertyName("status")] PodStatus? Status);

internal sealed record PodSpec(
    [property: JsonPropertyName("nodeName")] string? NodeName,
    [property: JsonPropertyName("containers")] IReadOnlyList<ContainerSpec>? Containers,
    [property: JsonPropertyName("volumes")] IReadOnlyList<Volume>? Volumes = null);

internal sealed record ContainerSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("volumeMounts")] IReadOnlyList<VolumeMount>? VolumeMounts = null);

/// <summary>A pod volume; only the persistent kinds are read (claims, host paths).</summary>
internal sealed record Volume(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("persistentVolumeClaim")] ClaimVolumeSource? PersistentVolumeClaim = null,
    [property: JsonPropertyName("hostPath")] HostPathVolumeSource? HostPath = null);

internal sealed record ClaimVolumeSource([property: JsonPropertyName("claimName")] string ClaimName);

internal sealed record HostPathVolumeSource([property: JsonPropertyName("path")] string Path);

internal sealed record VolumeMount(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mountPath")] string MountPath);

internal sealed record StatefulSetList([property: JsonPropertyName("items")] IReadOnlyList<StatefulSet> Items);

internal sealed record StatefulSet(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] StatefulSetSpec? Spec);

internal sealed record StatefulSetSpec(
    [property: JsonPropertyName("replicas")] int? Replicas,
    [property: JsonPropertyName("volumeClaimTemplates")] IReadOnlyList<ClaimTemplate>? VolumeClaimTemplates);

internal sealed record ClaimTemplate(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] ClaimTemplateSpec? Spec);

internal sealed record ClaimTemplateSpec([property: JsonPropertyName("resources")] ClaimResources? Resources);

internal sealed record ClaimResources([property: JsonPropertyName("requests")] IReadOnlyDictionary<string, string>? Requests);

internal sealed record PodStatus(
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("conditions")] IReadOnlyList<Condition>? Conditions,
    [property: JsonPropertyName("containerStatuses")] IReadOnlyList<ContainerStatus>? ContainerStatuses);

internal sealed record Condition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status);

internal sealed record ContainerStatus(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("started")] bool? Started,
    [property: JsonPropertyName("restartCount")] int RestartCount,
    [property: JsonPropertyName("state")] ContainerState? State,
    [property: JsonPropertyName("imageID")] string? ImageId = null);

internal sealed record ContainerState(
    [property: JsonPropertyName("running")] ContainerStateDetail? Running,
    [property: JsonPropertyName("waiting")] ContainerStateDetail? Waiting,
    [property: JsonPropertyName("terminated")] ContainerStateDetail? Terminated);

internal sealed record ContainerStateDetail(
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("exitCode")] int? ExitCode);

internal sealed record NodeList([property: JsonPropertyName("items")] IReadOnlyList<Node> Items);

internal sealed record Node(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("status")] NodeStatus? Status);

internal sealed record NodeStatus(
    [property: JsonPropertyName("capacity")] IReadOnlyDictionary<string, string>? Capacity,
    [property: JsonPropertyName("conditions")] IReadOnlyList<Condition>? Conditions,
    [property: JsonPropertyName("addresses")] IReadOnlyList<NodeAddress>? Addresses = null);

/// <summary>An address of a node: InternalIP, ExternalIP, Hostname…</summary>
internal sealed record NodeAddress(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("address")] string Address);

internal sealed record NodeMetricsList([property: JsonPropertyName("items")] IReadOnlyList<NodeMetrics> Items);

/// <summary>A node's usage, averaged by metrics-server over <c>window</c> (e.g. "20.064s") ending at <c>timestamp</c>.</summary>
internal sealed record NodeMetrics(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("usage")] IReadOnlyDictionary<string, string>? Usage,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp = null,
    [property: JsonPropertyName("window")] string? Window = null);

internal sealed record PodMetricsList([property: JsonPropertyName("items")] IReadOnlyList<PodMetrics> Items);

internal sealed record PodMetrics(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("containers")] IReadOnlyList<ContainerMetrics>? Containers);

internal sealed record ContainerMetrics(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("usage")] IReadOnlyDictionary<string, string>? Usage);

[JsonSerializable(typeof(PodList))]
[JsonSerializable(typeof(NodeList))]
[JsonSerializable(typeof(NodeMetricsList))]
[JsonSerializable(typeof(PodMetricsList))]
[JsonSerializable(typeof(PodMetrics))]
[JsonSerializable(typeof(StatefulSetList))]
internal sealed partial class KubernetesJsonContext : JsonSerializerContext;
