namespace Portarr.Core.Maintenance;

/// <summary>
/// What is known of a running image against its registry (F-UPD-01): <c>UpToDate</c>; <c>Behind</c> a newer
/// version of the same tag shape; <c>NewerBuild</c>, a floating tag (<c>latest</c>) whose registry digest is no
/// longer the running one; <c>Floating</c>, a floating or digest-pinned tag Portarr cannot compare; <c>Unknown</c>
/// when the registry could not be read or the image has no repository; <c>Pending</c> before the first lookup.
/// </summary>
public enum ImageStatus
{
    UpToDate,
    Behind,
    NewerBuild,
    Floating,
    Unknown,
    Pending,
}

/// <summary>
/// The verdict for one running image. <c>Current</c>: the running tag, or the version its OCI label states for a
/// floating tag (<c>FromLabel</c>). <c>Latest</c>: the newest tag of the same shape. Dates come from the registry
/// when it gives them. <c>Reason</c> is a reason code for <c>Unknown</c> and <c>Floating</c>.
/// </summary>
public sealed record ImageCheck(
    ImageStatus Status,
    string? Current,
    string? Latest = null,
    VersionChange? Change = null,
    DateTimeOffset? CurrentAt = null,
    DateTimeOffset? LatestAt = null,
    string? Reason = null,
    bool FromLabel = false)
{
    /// <summary>A workload created from an image id: its repository and tag are lost.</summary>
    public const string ImageId = "image-id";

    /// <summary>A floating tag without a running digest or a version label to compare.</summary>
    public const string FloatingTag = "floating";

    /// <summary>An image pinned by digest only.</summary>
    public const string Pinned = "pinned";

    /// <summary>The OCI label that states the version an image was built from.</summary>
    public const string VersionLabel = "org.opencontainers.image.version";

    /// <summary>The OCI label that names the source repository of an image.</summary>
    public const string SourceLabel = "org.opencontainers.image.source";

    /// <summary>
    /// Compares a running image with what its registry lists. A version tag is compared with the newer tags of
    /// its shape. A floating tag is compared by digest when the runtime reports the running one (Kubernetes),
    /// else through its version label, but only when that label is itself a tag of the repository (a base
    /// image's label, such as an OS version, would be compared with unrelated tags otherwise).
    /// </summary>
    public static ImageCheck Evaluate(ImageReference image, string? versionLabel, string? runningDigest, RegistryLookup? lookup)
    {
        if (image.IsImageId)
        {
            return new ImageCheck(ImageStatus.Unknown, versionLabel, Reason: ImageId);
        }

        if (lookup is null)
        {
            return new ImageCheck(ImageStatus.Pending, image.Tag);
        }

        if (lookup.Error is { } error)
        {
            return new ImageCheck(ImageStatus.Unknown, image.Tag, Reason: error);
        }

        if (VersionTag.Parse(image.Tag) is { } version)
        {
            return Compare(version, lookup, fromLabel: false);
        }

        if (image.Tag is { } tag && runningDigest is not null && lookup.Find(tag) is { Digest: { } remote } published)
        {
            return string.Equals(remote, runningDigest, StringComparison.OrdinalIgnoreCase)
                ? new ImageCheck(ImageStatus.UpToDate, tag, CurrentAt: published.UpdatedAt)
                : new ImageCheck(ImageStatus.NewerBuild, tag, tag, LatestAt: published.UpdatedAt);
        }

        if (VersionTag.Parse(versionLabel) is { } labelled && lookup.Find(labelled.Tag) is not null)
        {
            return Compare(labelled, lookup, fromLabel: true);
        }

        return new ImageCheck(ImageStatus.Floating, image.Tag ?? (image.Digest is null ? null : ImageReference.ShortDigest(image.Digest)), Reason: image.Tag is null ? Pinned : FloatingTag);
    }

    private static ImageCheck Compare(VersionTag version, RegistryLookup lookup, bool fromLabel)
    {
        var currentAt = lookup.Find(version.Tag)?.UpdatedAt;
        var newest = version.NewestIn(lookup.Tags.Select(t => t.Name));
        return newest is null
            ? new ImageCheck(ImageStatus.UpToDate, version.Tag, CurrentAt: currentAt, FromLabel: fromLabel)
            : new ImageCheck(ImageStatus.Behind, version.Tag, newest.Tag, version.ChangeTo(newest), currentAt, lookup.Find(newest.Tag)?.UpdatedAt, FromLabel: fromLabel);
    }
}
