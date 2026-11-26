using System.Text.Json.Serialization;

namespace Portarr.Providers.Trivy;

// The scanner companion's API (scanner/, D-131): camelCase JSON, the answer of GET /v1/results and the body of POST /v1/scans.
// Everything is optional here: the Core normalises what is kept (severity vocabulary, https links, closed reasons, lengths).

internal sealed record ScanRequest([property: JsonPropertyName("targets")] IReadOnlyList<ScanRequestTarget> Targets);

/// <summary>What is scanned: <c>kind</c> is <c>image</c> or <c>release</c> (the SBOM of a repository's latest release), <c>ref</c> its reference (<c>owner/name</c> for a release).</summary>
internal sealed record ScanRequestTarget([property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("ref")] string Ref);

internal sealed record ScannerResults(
    [property: JsonPropertyName("generatedAt")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("database")] ScannerDatabase? Database,
    [property: JsonPropertyName("images")] IReadOnlyList<ScannerImage>? Images);

internal sealed record ScannerDatabase(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt);

internal sealed record ScannerImage(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("ref")] string? Ref,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("scannedAt")] DateTimeOffset? ScannedAt,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("counts")] ScannerCounts? Counts,
    [property: JsonPropertyName("fixable")] ScannerCounts? Fixable,
    [property: JsonPropertyName("findings")] IReadOnlyList<ScannerFinding>? Findings,
    [property: JsonPropertyName("ids")] IReadOnlyList<string?>? Ids = null,
    [property: JsonPropertyName("idsTruncated")] bool? IdsTruncated = null);

internal sealed record ScannerCounts(
    [property: JsonPropertyName("critical")] int Critical,
    [property: JsonPropertyName("high")] int High,
    [property: JsonPropertyName("medium")] int Medium,
    [property: JsonPropertyName("low")] int Low,
    [property: JsonPropertyName("unknown")] int Unknown);

internal sealed record ScannerFinding(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("package")] string? Package,
    [property: JsonPropertyName("installedVersion")] string? InstalledVersion,
    [property: JsonPropertyName("fixedVersion")] string? FixedVersion,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url);

[JsonSerializable(typeof(ScanRequest))]
[JsonSerializable(typeof(ScannerResults))]
internal sealed partial class ScannerJsonContext : JsonSerializerContext;
