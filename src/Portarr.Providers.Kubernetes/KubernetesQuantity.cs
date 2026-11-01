using System.Globalization;

namespace Portarr.Providers.Kubernetes;

/// <summary>
/// Kubernetes resource quantities: <c>250m</c> or <c>40122367n</c> CPU, <c>1238720Ki</c> or <c>2G</c> memory,
/// plain or exponent numbers (<c>4</c>, <c>1e3</c>).
/// </summary>
internal static class KubernetesQuantity
{
    private static readonly (string Suffix, decimal Factor)[] Suffixes =
    [
        ("Ki", 1024m), ("Mi", 1024m * 1024), ("Gi", 1024m * 1024 * 1024), ("Ti", 1024m * 1024 * 1024 * 1024),
        ("Pi", 1024m * 1024 * 1024 * 1024 * 1024), ("Ei", 1024m * 1024 * 1024 * 1024 * 1024 * 1024),
        ("n", 0.000000001m), ("u", 0.000001m), ("m", 0.001m),
        ("k", 1_000m), ("M", 1_000_000m), ("G", 1_000_000_000m), ("T", 1_000_000_000_000m),
        ("P", 1_000_000_000_000_000m), ("E", 1_000_000_000_000_000_000m),
    ];

    public static bool TryParse(string? value, out decimal quantity)
    {
        quantity = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        foreach (var (suffix, factor) in Suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && TryNumber(text[..^suffix.Length], out var number))
            {
                quantity = number * factor;
                return true;
            }
        }

        return TryNumber(text, out quantity);
    }

    /// <summary>CPU in cores, or null when absent or invalid.</summary>
    public static double? Cores(IReadOnlyDictionary<string, string>? resources)
    {
        return resources is not null && TryParse(resources.GetValueOrDefault("cpu"), out var cores) ? (double)Math.Round(cores, 3) : null;
    }

    /// <summary>Memory (or another resource in bytes, e.g. <c>storage</c>), or null when absent or invalid.</summary>
    public static long? Bytes(IReadOnlyDictionary<string, string>? resources, string resource = "memory")
    {
        return resources is not null && TryParse(resources.GetValueOrDefault(resource), out var bytes) && bytes <= long.MaxValue ? (long)bytes : null;
    }

    private static bool TryNumber(string text, out decimal number)
    {
        // "1e3" is valid (AllowExponent); signs are not, a quantity Portarr reads is never negative.
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number);
    }
}
