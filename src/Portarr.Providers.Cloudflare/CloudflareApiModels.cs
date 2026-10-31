using System.Text.Json.Serialization;

namespace Portarr.Providers.Cloudflare;

// Subset of the Cloudflare API v4 responses Portarr reads.

internal sealed record CloudflareEnvelope<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("result")] T? Result,
    [property: JsonPropertyName("result_info")] ResultInfo? ResultInfo);

internal sealed record ResultInfo(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("total_pages")] int? TotalPages);

internal sealed record PagesProject(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_on")] DateTimeOffset? CreatedOn,
    [property: JsonPropertyName("production_branch")] string? ProductionBranch,
    [property: JsonPropertyName("canonical_deployment")] PagesDeployment? CanonicalDeployment,
    [property: JsonPropertyName("latest_deployment")] PagesDeployment? LatestDeployment);

internal sealed record PagesDeployment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("environment")] string? Environment,
    [property: JsonPropertyName("created_on")] DateTimeOffset? CreatedOn,
    [property: JsonPropertyName("latest_stage")] PagesStage? LatestStage,
    [property: JsonPropertyName("deployment_trigger")] PagesTrigger? DeploymentTrigger);

internal sealed record PagesStage(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status);

internal sealed record PagesTrigger([property: JsonPropertyName("metadata")] PagesTriggerMetadata? Metadata);

internal sealed record PagesTriggerMetadata(
    [property: JsonPropertyName("branch")] string? Branch,
    [property: JsonPropertyName("commit_hash")] string? CommitHash);

internal sealed record WorkerScript(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_on")] DateTimeOffset? CreatedOn,
    [property: JsonPropertyName("modified_on")] DateTimeOffset? ModifiedOn);

[JsonSerializable(typeof(CloudflareEnvelope<List<PagesProject>>))]
[JsonSerializable(typeof(CloudflareEnvelope<List<WorkerScript>>))]
internal sealed partial class CloudflareJsonContext : JsonSerializerContext;
