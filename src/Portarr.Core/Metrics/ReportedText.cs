using System.Globalization;

namespace Portarr.Core.Metrics;

/// <summary>
/// Text a machine or an exporter reports about itself (an operating system, a card's name): it comes over the network from something
/// Portarr does not control, and it ends up on a page and in the answers given to an assistant. It is trimmed, stripped of control
/// characters and cut to a length that holds any real name.
/// </summary>
public static class ReportedText
{
    /// <summary>The longest reported text kept: far more than any operating system, kernel or card name needs.</summary>
    public const int MaxLength = 128;

    /// <summary>Control characters and the invisible ones that can reorder or hide text (format characters such as a right-to-left override, separators, private use, lone surrogates) are dropped.</summary>
    private static bool IsPlain(char c)
    {
        return !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned);
    }

    /// <summary>The text cleaned, or null when nothing is left.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string(text.Where(IsPlain).Take(MaxLength).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
