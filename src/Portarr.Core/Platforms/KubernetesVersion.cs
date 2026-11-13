using System.Globalization;
using System.Text.RegularExpressions;

namespace Portarr.Core.Platforms;

/// <summary>
/// The version of a Kubernetes distribution as a kubelet reports it: <c>v1.31.4+k3s1</c> is Kubernetes 1.31.4, build 1 of K3s.
/// Versions compare as Kubernetes does (major, minor, patch, a release over its pre-release), then by build. <see cref="Line"/>
/// is the minor line (<c>1.31</c>): Kubernetes supports a few lines at a time and a cluster moves one line at a time.
/// </summary>
public sealed partial record KubernetesVersion(int Major, int Minor, int Patch, bool PreRelease, string? Distribution, int Build, string Text) : IComparable<KubernetesVersion>
{
    public const string K3s = "k3s";

    /// <summary>The minor line, <c>1.31</c>.</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public bool IsK3s => Distribution == K3s;

    [GeneratedRegex(@"^v?(?<major>[0-9]{1,3})\.(?<minor>[0-9]{1,3})\.(?<patch>[0-9]{1,4})(?<pre>-[0-9A-Za-z.]+)?(?:\+(?<build>[0-9A-Za-z]+))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();


    /// <summary>Reads a version as a kubelet or the K3s release channels write it; null when it is not one.</summary>
    public static KubernetesVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Pattern().Match(text.Trim()) is not { Success: true } match)
        {
            return null;
        }

        string? distribution = null;
        var build = 0;
        if (match.Groups["build"] is { Success: true } buildGroup)
        {
            // "k3s1": the distribution "k3s" and its build 1; anything else (rke2r1, eks-1) keeps its text as the distribution.
            if (buildGroup.Value.StartsWith(K3s, StringComparison.Ordinal) && int.TryParse(buildGroup.Value[K3s.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out build))
            {
                distribution = K3s;
            }
            else
            {
                distribution = buildGroup.Value;
            }
        }

        return new KubernetesVersion(
            int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture),
            match.Groups["pre"].Success,
            distribution,
            build,
            text.Trim());
    }

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

        // 1.31.4-rc1 comes before 1.31.4.
        var byRelease = other.PreRelease.CompareTo(PreRelease);
        return byRelease != 0 ? byRelease : Build.CompareTo(other.Build);
    }

    public static bool operator <(KubernetesVersion? left, KubernetesVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator >(KubernetesVersion? left, KubernetesVersion? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator <=(KubernetesVersion? left, KubernetesVersion? right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >=(KubernetesVersion? left, KubernetesVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;
}
