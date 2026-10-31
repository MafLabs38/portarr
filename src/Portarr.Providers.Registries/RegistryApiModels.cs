using System.Text.Json.Serialization;

namespace Portarr.Providers.Registries;

// Subsets of the Docker Hub API, the OCI distribution API and the registry token endpoints Portarr reads.

internal sealed record HubTagPage(
    [property: JsonPropertyName("next")] string? Next,
    [property: JsonPropertyName("results")] IReadOnlyList<HubTag>? Results);

internal sealed record HubTag(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("last_updated")] DateTimeOffset? LastUpdated,
    [property: JsonPropertyName("digest")] string? Digest);

internal sealed record OciTagList([property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags);

internal sealed record RegistryToken(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("access_token")] string? AccessToken);

/// <summary>An image index (multi-architecture) or a single manifest: one of the two lists is set.</summary>
internal sealed record OciManifest(
    [property: JsonPropertyName("manifests")] IReadOnlyList<OciDescriptor>? Manifests,
    [property: JsonPropertyName("config")] OciDescriptor? Config);

internal sealed record OciDescriptor(
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("platform")] OciPlatform? Platform);

internal sealed record OciPlatform(
    [property: JsonPropertyName("os")] string? Os,
    [property: JsonPropertyName("architecture")] string? Architecture);

internal sealed record OciConfig([property: JsonPropertyName("created")] DateTimeOffset? Created);

[JsonSerializable(typeof(HubTagPage))]
[JsonSerializable(typeof(HubTag))]
[JsonSerializable(typeof(OciTagList))]
[JsonSerializable(typeof(RegistryToken))]
[JsonSerializable(typeof(OciManifest))]
[JsonSerializable(typeof(OciConfig))]
internal sealed partial class RegistryJsonContext : JsonSerializerContext;
