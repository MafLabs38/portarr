namespace Portarr.Core.Platforms;

/// <summary>The kinds of platform Portarr reports on (D-119): <c>os</c>, <c>kubernetes</c> for a plain kubelet, and the name of a distribution (<c>k3s</c>…) for the others.</summary>
public static class PlatformKinds
{
    public const string Kubernetes = "kubernetes";
    public const string Os = "os";
}

/// <summary>
/// Where the lookup of a platform's support dates and newer versions stands (D-119), so that "the sources do not list it", "the lookup
/// failed", "the lookups are off" and "not read yet" are never the same grey pill.
/// </summary>
public static class PlatformLookups
{
    /// <summary>The support date of this version is known.</summary>
    public const string Ok = "ok";

    /// <summary>Not read yet (a few minutes after start, or after a machine appears).</summary>
    public const string Pending = "pending";

    /// <summary>The sources do not list this product or this version: Portarr says what it does not know.</summary>
    public const string NotListed = "not-listed";

    /// <summary>The lookup failed (a source is down, outbound traffic is blocked) and no earlier answer is kept.</summary>
    public const string Failed = "failed";

    /// <summary>The operator switched the lookups off (<c>PORTARR__PLATFORMUPDATES=false</c>).</summary>
    public const string Disabled = "disabled";
}

/// <summary>A release line of a platform and the newest version published on it: <c>1.31</c> and <c>v1.31.4+k3s1</c>.</summary>
public sealed record ReleaseLine(string Line, string Latest);

/// <summary>
/// Capability of a connector that knows the releases of a piece of platform software (D-119): K3s publishes the newest version of
/// every Kubernetes line it ships. Read-only (GET), cached for hours by the monitor. A connector joins the contract suite
/// (CLAUDE.md rule 8).
/// </summary>
public interface IReleaseLineSource
{
    /// <summary>
    /// The platform whose releases this source lists: the distribution a kubelet version names after its plus sign (<c>k3s</c> for
    /// <c>v1.31.4+k3s1</c>). Core asks for the lines of every distribution the nodes run; the source that carries that name answers.
    /// </summary>
    string Platform { get; }

    /// <summary>The platform as it is shown to the operator (<c>K3s</c>).</summary>
    string DisplayName { get; }

    /// <summary>One line per published release line, each with its newest version; throws when the source cannot be read.</summary>
    Task<IReadOnlyList<ReleaseLine>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One release cycle of a product and when it stops being supported: <c>Cycle</c> is the version as the product numbers its lines
/// (<c>25.10</c>, <c>1.31</c>); <c>EndOfActiveSupport</c> the day active support ends, <c>EndOfLife</c> the day security fixes stop.
/// A missing date is a date nobody has announced, not an infinite support.
/// </summary>
public sealed record SupportCycle(string Cycle, DateOnly? EndOfActiveSupport, DateOnly? EndOfLife);

/// <summary>
/// Capability of a connector that knows when the versions of a product stop being supported (D-119): endoflife.date lists
/// operating systems and Kubernetes. Read-only, cached for hours. A connector joins the contract suite (rule 8).
/// </summary>
public interface ISupportLifecycleSource
{
    /// <summary>
    /// The cycles of <paramref name="product"/> (the identifier the source uses: <c>ubuntu</c>, <c>kubernetes</c>), newest first, or null
    /// when the source does not know the product. Throws when the source cannot be read.
    /// </summary>
    Task<IReadOnlyList<SupportCycle>?> ReadAsync(string product, CancellationToken cancellationToken);
}
