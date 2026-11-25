using System.Text.Json.Serialization;

namespace Portarr.Providers.GitHub;

// Subset of the GitHub REST API responses Portarr reads.

internal sealed record GitHubAccount(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("type")] string? Type);

internal sealed record GitHubRepository(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] GitHubAccount Owner,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("private")] bool Private,
    [property: JsonPropertyName("visibility")] string? Visibility,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("fork")] bool Fork);

internal sealed record GitHubError([property: JsonPropertyName("message")] string? Message);

internal sealed record DependabotAlert(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("dependency")] DependabotDependency? Dependency,
    [property: JsonPropertyName("security_advisory")] SecurityAdvisory? SecurityAdvisory,
    [property: JsonPropertyName("security_vulnerability")] SecurityVulnerability? SecurityVulnerability);

internal sealed record DependabotDependency(
    [property: JsonPropertyName("package")] AdvisoryPackage? Package,
    [property: JsonPropertyName("manifest_path")] string? ManifestPath);

internal sealed record AdvisoryPackage(
    [property: JsonPropertyName("ecosystem")] string? Ecosystem,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record SecurityAdvisory(
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("ghsa_id")] string? GhsaId,
    [property: JsonPropertyName("cve_id")] string? CveId,
    [property: JsonPropertyName("summary")] string? Summary);

internal sealed record SecurityVulnerability(
    [property: JsonPropertyName("package")] AdvisoryPackage? Package,
    [property: JsonPropertyName("vulnerable_version_range")] string? VulnerableVersionRange,
    [property: JsonPropertyName("first_patched_version")] PatchedVersion? FirstPatchedVersion);

internal sealed record PatchedVersion([property: JsonPropertyName("identifier")] string? Identifier);

internal sealed record CodeScanningAlert(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("rule")] CodeScanningRule? Rule,
    [property: JsonPropertyName("most_recent_instance")] CodeScanningInstance? MostRecentInstance);

/// <summary><c>security_severity_level</c> is set for security rules only; quality rules have a plain <c>severity</c>.</summary>
internal sealed record CodeScanningRule(
    [property: JsonPropertyName("security_severity_level")] string? SecuritySeverityLevel,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("description")] string? Description);

internal sealed record CodeScanningInstance([property: JsonPropertyName("location")] CodeScanningLocation? Location);

internal sealed record CodeScanningLocation(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("start_line")] int? StartLine);

/// <summary>The secret itself is never read: the request asks GitHub to hide it, and no field of it is mapped.</summary>
internal sealed record SecretScanningAlert(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("secret_type_display_name")] string? SecretTypeDisplayName);

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

internal sealed record GitHubTag([property: JsonPropertyName("name")] string Name);

internal sealed record PullRequest([property: JsonPropertyName("user")] GitHubAccount? User);

internal sealed record WorkflowRuns([property: JsonPropertyName("workflow_runs")] IReadOnlyList<WorkflowRun>? Runs);

internal sealed record WorkflowRun(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl);

[JsonSerializable(typeof(GitHubAccount))]
[JsonSerializable(typeof(GitHubError))]
[JsonSerializable(typeof(GitHubRepository))]
[JsonSerializable(typeof(List<GitHubRepository>))]
[JsonSerializable(typeof(List<DependabotAlert>))]
[JsonSerializable(typeof(List<CodeScanningAlert>))]
[JsonSerializable(typeof(List<SecretScanningAlert>))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubTag>))]
[JsonSerializable(typeof(List<PullRequest>))]
[JsonSerializable(typeof(WorkflowRuns))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
