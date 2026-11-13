using System.Globalization;
using System.Text.RegularExpressions;

namespace Portarr.Core.Platforms;

/// <summary>
/// The version of a Kubernetes distribution as a kubelet reports it: <c>v1.31.4+k3s1</c> is Kubernetes 1.31.4, build 1 of K3s.
/// Versions compare as Kubernetes does (major, minor, patch, a release over its pre-release), then by build. <see cref="Line"/>
/// is the minor line (<c>1.31</c>): Kubernetes supports a few lines at a time and a cluster moves one line at a time. The build after the
/// plus sign is read generically: its letters name the distribution (<c>k3s</c>, <c>rke2r</c>) and its trailing number is the build.
/// </summary>
public sealed partial record KubernetesVersion(int Major, int Minor, int Patch, string? Pre, string? Distribution, int Build, string Text) : IComparable<KubernetesVersion>
{
    /// <summary>True for a pre-release (<c>1.31.4-rc1</c>): it comes before the release it leads to.</summary>
    public bool PreRelease => Pre is not null;

    /// <summary>The minor line, <c>1.31</c>.</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    [GeneratedRegex(@"^v?(?<major>[0-9]{1,3})\.(?<minor>[0-9]{1,3})\.(?<patch>[0-9]{1,4})(?<pre>-[0-9A-Za-z.]{1,32})?(?:\+(?<build>[0-9A-Za-z]{1,32}))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();

    /// <summary>The build of a distribution: <c>k3s1</c> is <c>k3s</c> build 1, <c>rke2r1</c> is <c>rke2r</c> build 1, <c>k3s</c> alone is build 0.</summary>
    [GeneratedRegex("^(?<name>[0-9A-Za-z]*?)(?<number>[0-9]{1,9})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BuildPattern();

    /// <summary>Reads a version as a kubelet or the K3s release channels write it; null when it is not one.</summary>
    public static KubernetesVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Pattern().Match(text.Trim()) is not { Success: true } match)
        {
            return null;
        }

        string? distribution = null;
        var build = 0;
        // A build that does not read is no build: the distribution stays unknown rather than failing the node.
        if (match.Groups["build"] is { Success: true } buildGroup && BuildPattern().Match(buildGroup.Value) is { Success: true } parts)
        {
            distribution = parts.Groups["name"].Value is { Length: > 0 } name ? name.ToLowerInvariant() : null;
            build = int.Parse(parts.Groups["number"].Success ? parts.Groups["number"].Value : "0", NumberStyles.None, CultureInfo.InvariantCulture);
        }

        return new KubernetesVersion(
            int.Parse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null,
            distribution,
            build,
            text.Trim());
    }

    /// <summary>Two versions are equal when they compare equal: <c>v1.31.4</c> and <c>1.31.4</c> are the same version, whatever their text.</summary>
    public bool Equals(KubernetesVersion? other) => other is not null && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Pre, Build);

    public int CompareTo(KubernetesVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNumbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumbers != 0)
        {
            return byNumbers;
        }

        // 1.31.4-rc1 comes before 1.31.4; two pre-releases are told apart by their label, in text order.
        var byRelease = other.PreRelease.CompareTo(PreRelease);
        if (byRelease != 0 || !PreRelease)
        {
            return byRelease != 0 ? byRelease : Build.CompareTo(other.Build);
        }

        var byLabel = string.CompareOrdinal(Pre, other.Pre);
        return byLabel != 0 ? byLabel : Build.CompareTo(other.Build);
    }

    public static bool operator <(KubernetesVersion? left, KubernetesVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator >(KubernetesVersion? left, KubernetesVersion? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator <=(KubernetesVersion? left, KubernetesVersion? right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >=(KubernetesVersion? left, KubernetesVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;
}
