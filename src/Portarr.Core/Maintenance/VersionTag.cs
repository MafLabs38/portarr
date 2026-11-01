using System.Globalization;
using System.Text.RegularExpressions;

namespace Portarr.Core.Maintenance;

/// <summary>How far a newer version is: the first number that changes (F-UPD-01), or only a build suffix (<c>-ls12</c> → <c>-ls13</c>).</summary>
public enum VersionChange
{
    Build,
    Patch,
    Minor,
    Major,
}

/// <summary>
/// An image tag read as a version: a prefix (<c>v</c>, <c>version-</c>), dot-separated numbers and a suffix
/// (<c>-alpine</c>, <c>-ls12</c>). Two tags are comparable only when they have the same <see cref="Shape"/>:
/// same prefix, as many numbers, same suffix once its numbers are masked. So <c>16-alpine</c> is compared with
/// <c>17-alpine</c>, never with <c>17</c>, <c>17.1-alpine</c>, <c>18-rc1</c> or <c>amd64-17-alpine</c>.
/// </summary>
public sealed partial record VersionTag(string Tag, string Shape, IReadOnlyList<long> Numbers, IReadOnlyList<long> SuffixNumbers) : IComparable<VersionTag>
{
    /// <summary>Tags that name a moving target rather than a version.</summary>
    private static readonly HashSet<string> FloatingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "latest", "stable", "edge", "nightly", "develop", "dev", "main", "master", "release", "lts", "current", "rolling", "beta", "next", "preview",
    };

    /// <summary>A version tag, or null for a floating tag (<c>latest</c>, <c>stable</c>, a name without numbers).</summary>
    public static VersionTag? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || FloatingNames.Contains(tag) || tag.Length > 128)
        {
            return null;
        }

        var match = TagPattern().Match(tag);
        if (!match.Success)
        {
            return null;
        }

        var numbers = new List<long>();
        foreach (var part in match.Groups["numbers"].Value.Split('.'))
        {
            if (part.Length > 18)
            {
                return null;
            }

            numbers.Add(long.Parse(part, CultureInfo.InvariantCulture));
        }

        var suffix = match.Groups["suffix"].Value;
        var suffixNumbers = DigitsPattern().Matches(suffix)
            .Select(m => m.Value.Length > 18 ? 0 : long.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToList();
        var shape = $"{match.Groups["prefix"].Value.ToLowerInvariant()}<{numbers.Count}>{DigitsPattern().Replace(suffix.ToLowerInvariant(), "#")}";

        return new VersionTag(tag, shape, numbers, suffixNumbers);
    }

    /// <summary>Compares two tags of the same shape: numbers first, then the numbers of the suffix.</summary>
    public int CompareTo(VersionTag? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNumbers = Compare(Numbers, other.Numbers);
        return byNumbers != 0 ? byNumbers : Compare(SuffixNumbers, other.SuffixNumbers);
    }

    public static bool operator <(VersionTag? left, VersionTag? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(VersionTag? left, VersionTag? right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(VersionTag? left, VersionTag? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(VersionTag? left, VersionTag? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    /// <summary>How far <paramref name="newer"/> is from this tag (same shape, newer).</summary>
    public VersionChange ChangeTo(VersionTag newer)
    {
        for (var i = 0; i < Math.Min(Numbers.Count, newer.Numbers.Count); i++)
        {
            if (Numbers[i] != newer.Numbers[i])
            {
                return i switch
                {
                    0 => VersionChange.Major,
                    1 => VersionChange.Minor,
                    _ => VersionChange.Patch,
                };
            }
        }

        return VersionChange.Build;
    }

    /// <summary>The newest tag of the same shape that is newer than this one, or null when this one is the newest.</summary>
    public VersionTag? NewestIn(IEnumerable<string> tags)
    {
        VersionTag? newest = null;
        foreach (var candidate in tags.Select(Parse))
        {
            if (candidate is not null && candidate.Shape == Shape && candidate.CompareTo(newest ?? this) > 0)
            {
                newest = candidate;
            }
        }

        return newest;
    }

    private static int Compare(IReadOnlyList<long> left, IReadOnlyList<long> right)
    {
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    /// <summary>Prefix of letters (and one separator), then numbers, then anything.</summary>
    [GeneratedRegex("^(?<prefix>[A-Za-z]*[-_.]?)(?<numbers>[0-9]+(\\.[0-9]+)*)(?<suffix>[-_+.A-Za-z][A-Za-z0-9._+-]*)?$")]
    private static partial Regex TagPattern();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DigitsPattern();
}
