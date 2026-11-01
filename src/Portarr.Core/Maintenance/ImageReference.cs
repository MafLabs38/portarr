using System.Text.RegularExpressions;

namespace Portarr.Core.Maintenance;

/// <summary>
/// A container image reference as a workload runs it: <c>registry/repository:tag@digest</c>, normalized like the
/// Docker CLI does (<c>postgres:16</c> is <c>docker.io/library/postgres:16</c>). A workload created from a bare
/// image id (<c>sha256:…</c>) has no repository: <see cref="IsImageId"/>, nothing can be looked up.
/// </summary>
public sealed partial record ImageReference(string Registry, string Repository, string? Tag, string? Digest, bool IsImageId = false)
{
    public const string DockerHub = "docker.io";

    /// <summary>Registry and repository: the key of a registry lookup (<c>docker.io/library/postgres</c>).</summary>
    public string RepositoryKey => $"{Registry}/{Repository}";

    /// <summary>The reference as people write it: Docker Hub's prefixes are dropped (<c>postgres:16</c>, <c>grafana/grafana:latest</c>).</summary>
    public string Display
    {
        get
        {
            if (IsImageId)
            {
                return Digest is null ? string.Empty : ShortDigest(Digest);
            }

            var name = Registry == DockerHub ? WithoutLibraryPrefix(Repository) : RepositoryKey;
            return name + (Tag is null ? string.Empty : $":{Tag}") + (Digest is null || Tag is not null ? string.Empty : $"@{ShortDigest(Digest)}");
        }
    }

    private static string WithoutLibraryPrefix(string repository) =>
        repository.StartsWith("library/", StringComparison.Ordinal) ? repository["library/".Length..] : repository;

    public static string ShortDigest(string digest)
    {
        var hex = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest["sha256:".Length..] : digest;
        return $"sha256:{hex[..Math.Min(12, hex.Length)]}";
    }

    /// <summary>Null for an empty or malformed reference (a Cloudflare project, a display name).</summary>
    public static ImageReference? Parse(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (ImageIdPattern().IsMatch(text))
        {
            return new ImageReference(string.Empty, string.Empty, null, text.StartsWith("sha256:", StringComparison.Ordinal) ? text : $"sha256:{text}", IsImageId: true);
        }

        if (!ReferencePattern().IsMatch(text))
        {
            return null;
        }

        text = TakeDigest(text, out var digest);
        text = TakeTag(text, out var tag);
        var (registry, repository) = SplitRegistry(text);

        if (tag is null && digest is null)
        {
            tag = "latest";
        }

        return repository.Length == 0 ? null : new ImageReference(registry, repository, tag, digest);
    }

    private static string TakeDigest(string text, out string? digest)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        digest = at >= 0 ? text[(at + 1)..] : null;
        return at >= 0 ? text[..at] : text;
    }

    private static string TakeTag(string text, out string? tag)
    {
        var colon = text.LastIndexOf(':');
        var hasTag = colon > text.LastIndexOf('/');
        tag = hasTag ? text[(colon + 1)..] : null;
        return hasTag ? text[..colon] : text;
    }

    /// <summary>The registry (Docker Hub when the first segment is not an address) and the repository, lower-cased, with the Hub's aliases and its <c>library/</c> namespace resolved.</summary>
    private static (string Registry, string Repository) SplitRegistry(string text)
    {
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        var first = slash < 0 ? string.Empty : text[..slash];
        var hasRegistry = first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || first == "localhost";
        var registry = hasRegistry ? first.ToLowerInvariant() : DockerHub;
        var repository = (hasRegistry ? text[(slash + 1)..] : text).ToLowerInvariant();

        if (registry is "index.docker.io" or "registry-1.docker.io" or "registry.hub.docker.com")
        {
            registry = DockerHub;
        }

        if (registry == DockerHub && !repository.Contains('/', StringComparison.Ordinal))
        {
            repository = $"library/{repository}";
        }

        return (registry, repository);
    }

    [GeneratedRegex("^(sha256:)?[0-9a-f]{64}$")]
    private static partial Regex ImageIdPattern();

    /// <summary>Loose on purpose: names, a tag, a digest; no spaces.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/:-]*(@sha256:[0-9a-f]{64})?$")]
    private static partial Regex ReferencePattern();
}
