using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portarr.Providers.Kubernetes;

// Subset of the Kubernetes API (core/v1, apps/v1, batch/v1 and metrics.k8s.io/v1beta1) responses Portarr reads.

internal sealed record ListMetadata([property: JsonPropertyName("continue")] string? Continue);

internal sealed record ObjectMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("namespace")] string? Namespace,
    [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels,
    [property: JsonPropertyName("creationTimestamp")] DateTimeOffset? CreationTimestamp,
    [property: JsonPropertyName("deletionTimestamp")] DateTimeOffset? DeletionTimestamp = null,
    [property: JsonPropertyName("ownerReferences")] IReadOnlyList<OwnerReference>? OwnerReferences = null);

internal sealed record OwnerReference(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name);

/// <summary>A paginated list of any kind (<c>items</c> and the continue token).</summary>
internal sealed record ObjectList<T>(
    [property: JsonPropertyName("metadata")] ListMetadata? Metadata,
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items);

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

/// <summary>A CronJob's status: when it was last scheduled and when a job it started last succeeded.</summary>
internal sealed record CronJob(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] CronJobSpec? Spec,
    [property: JsonPropertyName("status")] CronJobStatus? Status);

internal sealed record CronJobSpec(
    [property: JsonPropertyName("schedule")] string? Schedule,
    [property: JsonPropertyName("suspend")] bool? Suspend);

internal sealed record CronJobStatus(
    [property: JsonPropertyName("lastScheduleTime")] DateTimeOffset? LastScheduleTime,
    [property: JsonPropertyName("lastSuccessfulTime")] DateTimeOffset? LastSuccessfulTime);

/// <summary>A batch Job; its conditions say whether it completed or failed.</summary>
internal sealed record BatchJob(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("status")] BatchJobStatus? Status);

internal sealed record BatchJobStatus(
    [property: JsonPropertyName("conditions")] IReadOnlyList<Condition>? Conditions,
    [property: JsonPropertyName("completionTime")] DateTimeOffset? CompletionTime);

// K8up (k8up.io/v1, operator v2.16, chart 4.10): Schedule, Backup and Snapshot custom resources.

internal sealed record K8upSchedule(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] K8upScheduleSpec? Spec);

internal sealed record K8upScheduleSpec(
    [property: JsonPropertyName("backup")] K8upBackupSchedule? Backup,
    [property: JsonPropertyName("backend")] K8upBackend? Backend);

internal sealed record K8upBackupSchedule(
    [property: JsonPropertyName("schedule")] string? Schedule,
    [property: JsonPropertyName("backend")] K8upBackend? Backend);

/// <summary>Where a schedule writes; only the kind and the identifiers that form restic's repository string are read, never a secret reference.</summary>
internal sealed record K8upBackend(
    [property: JsonPropertyName("s3")] K8upS3? S3,
    [property: JsonPropertyName("b2")] K8upB2? B2,
    [property: JsonPropertyName("local")] K8upLocal? Local,
    [property: JsonPropertyName("gcs")] JsonElement? Gcs,
    [property: JsonPropertyName("azure")] JsonElement? Azure,
    [property: JsonPropertyName("swift")] JsonElement? Swift,
    [property: JsonPropertyName("rest")] JsonElement? Rest);

internal sealed record K8upS3(
    [property: JsonPropertyName("endpoint")] string? Endpoint,
    [property: JsonPropertyName("bucket")] string? Bucket);

internal sealed record K8upB2(
    [property: JsonPropertyName("bucket")] string? Bucket,
    [property: JsonPropertyName("path")] string? Path);

internal sealed record K8upLocal([property: JsonPropertyName("mountPath")] string? MountPath);

/// <summary>A K8up Backup: its <c>Completed</c> condition says whether it succeeded or failed.</summary>
internal sealed record K8upBackup(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("status")] K8upStatus? Status);

internal sealed record K8upStatus(
    [property: JsonPropertyName("started")] bool? Started,
    [property: JsonPropertyName("finished")] bool? Finished,
    [property: JsonPropertyName("conditions")] IReadOnlyList<Condition>? Conditions);

/// <summary>A restic snapshot K8up keeps in the cluster after a backup: id, date, paths and repository.</summary>
internal sealed record K8upSnapshot(
    [property: JsonPropertyName("metadata")] ObjectMetadata Metadata,
    [property: JsonPropertyName("spec")] K8upSnapshotSpec? Spec);

internal sealed record K8upSnapshotSpec(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("date")] DateTimeOffset? Date,
    [property: JsonPropertyName("paths")] IReadOnlyList<string>? Paths,
    [property: JsonPropertyName("repository")] string? Repository);

internal sealed record PodStatus(
    [property: JsonPropertyName("phase")] string? Phase,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("conditions")] IReadOnlyList<Condition>? Conditions,
    [property: JsonPropertyName("containerStatuses")] IReadOnlyList<ContainerStatus>? ContainerStatuses);

internal sealed record Condition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("lastTransitionTime")] DateTimeOffset? LastTransitionTime = null);

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
[JsonSerializable(typeof(CronJob))]
[JsonSerializable(typeof(ObjectList<CronJob>))]
[JsonSerializable(typeof(ObjectList<BatchJob>))]
[JsonSerializable(typeof(ObjectList<K8upSchedule>))]
[JsonSerializable(typeof(ObjectList<K8upBackup>))]
[JsonSerializable(typeof(ObjectList<K8upSnapshot>))]
internal sealed partial class KubernetesJsonContext : JsonSerializerContext;
