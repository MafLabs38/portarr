namespace Portarr.Core.Platforms;

/// <summary>The kinds of platform Portarr reports on (D-119).</summary>
public static class PlatformKinds
{
    public const string K3s = "k3s";
    public const string Kubernetes = "kubernetes";
    public const string Os = "os";
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
