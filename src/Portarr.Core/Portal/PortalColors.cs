using System.Text.RegularExpressions;

namespace Portarr.Core.Portal;

/// <summary>The colors of the identity badge, resolved: each one is a theme token name or a hex color (see <see cref="PortalColor"/>).</summary>
public sealed record PortalColors(string Name, string Version)
{
    /// <summary>Portarr's accent (the yellow of the logo) for both texts.</summary>
    public static PortalColors Default { get; } = new(PortalColor.Default, PortalColor.Default);
}

/// <summary>
/// The rules for the badge colors (D-91): a hex color (<c>#rgb</c>, <c>#rrggbb</c>) or the name of a theme token from a
/// fixed list. Nothing else is accepted, so a value can never carry CSS beyond one color.
/// </summary>
public static partial class PortalColor
{
    /// <summary>Portarr's yellow, used when the catalog says nothing.</summary>
    public const string Default = "accent";

    public static IReadOnlyList<string> Tokens { get; } = ["accent", "accentDeep", "secondary", "ink", "inkSoft", "inkMuted"];

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexPattern();

    /// <summary>Whether the value is a hex color or one of <see cref="Tokens"/>.</summary>
    public static bool IsValid(string value) => HexPattern().IsMatch(value) || Tokens.Contains(value, StringComparer.Ordinal);

    /// <summary>The reason a color is refused (<paramref name="field"/> is <c>colors.name</c> or <c>colors.version</c>), or null.</summary>
    public static string? Validate(string field, string value)
    {
        return IsValid(value) ? null : $"{field} must be a hex color (#rgb or #rrggbb) or one of: {string.Join(", ", Tokens)}.";
    }
}
