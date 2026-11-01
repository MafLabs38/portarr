namespace Portarr.Core.Maintenance;

/// <summary>A tag of a registry repository. <c>UpdatedAt</c> and <c>Digest</c> are set when the registry says them cheaply.</summary>
public sealed record RegistryTag(string Name, DateTimeOffset? UpdatedAt = null, string? Digest = null);

/// <summary>
/// What a registry said about one repository (<c>docker.io/library/postgres</c>): its tags, or why it could not
/// be read (<c>Error</c>, a reason code: <c>not-found</c>, <c>refused</c>, <c>rate-limited</c>, <c>unreachable</c>…).
/// </summary>
public sealed record RegistryLookup(IReadOnlyList<RegistryTag> Tags, string? Error, DateTimeOffset CheckedAt)
{
    public RegistryTag? Find(string tag) => Tags.FirstOrDefault(t => t.Name == tag);
}

/// <summary>
/// Capability of a container registry connector (D-66): anonymous pulls-only access (Docker Hub, GHCR, lscr.io,
/// Quay, any OCI registry). GET and HEAD requests only, never a push; a lookup is cached for hours.
/// </summary>
public interface IImageRegistry
{
    /// <summary>The tags of the image's repository, newest first when the registry can sort them.</summary>
    Task<IReadOnlyList<RegistryTag>> ListTagsAsync(ImageReference image, CancellationToken cancellationToken);

    /// <summary>The digest and creation date of one tag (a manifest read), or null when the tag does not exist.</summary>
    Task<RegistryTag?> DescribeAsync(ImageReference image, string tag, CancellationToken cancellationToken);
}

/// <summary>Thrown by a registry connector that cannot read a repository; <c>Reason</c> is a reason code.</summary>
public sealed class RegistryUnavailableException : Exception
{
    public const string NotFound = "not-found";
    public const string Refused = "refused";
    public const string RateLimited = "rate-limited";

    public RegistryUnavailableException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public RegistryUnavailableException()
        : this("unavailable")
    {
    }

    public RegistryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = message;
    }

    public string Reason { get; }
}
