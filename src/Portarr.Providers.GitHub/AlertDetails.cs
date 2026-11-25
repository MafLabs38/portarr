using System.Globalization;
using System.Text;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.GitHub;

/// <summary>
/// Reads the alerts GitHub returns as <see cref="AlertDetail"/>: what is wrong, where, and which version mends it.
/// Text comes from third parties (an advisory's summary): control, format and separator characters are dropped and it
/// is cut short. Severity and link rules live in Core (<see cref="AlertDetail.Normalised"/>).
/// </summary>
internal static class AlertDetails
{
    private const int MaxText = 200;

    public static List<AlertDetail> Dependabot(List<DependabotAlert> alerts) =>
        [.. alerts.Select(a =>
        {
            var package = a.SecurityVulnerability?.Package ?? a.Dependency?.Package;
            return new AlertDetail(
                AlertSources.Dependabot,
                Id(a.Number),
                a.SecurityAdvisory?.Severity,
                Text(a.SecurityAdvisory?.Summary) ?? Text(a.SecurityAdvisory?.GhsaId) ?? "Dependency alert",
                Text(package?.Name),
                Text(package?.Ecosystem),
                Text(a.SecurityVulnerability?.VulnerableVersionRange),
                Text(a.SecurityVulnerability?.FirstPatchedVersion?.Identifier),
                Text(a.SecurityAdvisory?.GhsaId),
                Text(a.SecurityAdvisory?.CveId),
                Text(a.Dependency?.ManifestPath),
                Link(a.HtmlUrl),
                a.CreatedAt);
        })];

    public static List<AlertDetail> CodeScanning(List<CodeScanningAlert> alerts) =>
        [.. alerts.Select(a =>
        {
            var location = a.MostRecentInstance?.Location;
            return new AlertDetail(
                AlertSources.CodeScanning,
                Id(a.Number),
                a.Rule?.SecuritySeverityLevel,
                Text(a.Rule?.Description) ?? Text(a.Rule?.Id) ?? "Code scanning alert",
                null,
                null,
                null,
                null,
                null,
                null,
                Place(location),
                Link(a.HtmlUrl),
                a.CreatedAt);
        })];

    public static List<AlertDetail> Secrets(List<SecretScanningAlert> alerts) =>
        [.. alerts.Select(a => new AlertDetail(
            AlertSources.SecretScanning,
            Id(a.Number),
            null,
            Text(a.SecretTypeDisplayName) ?? "Secret",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Link(a.HtmlUrl),
            a.CreatedAt))];

    private static string Id(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string? Place(CodeScanningLocation? location)
    {
        if (string.IsNullOrWhiteSpace(location?.Path))
        {
            return null;
        }

        return Text(location.StartLine is { } line ? $"{location.Path}:{line}" : location.Path);
    }

    private static string? Text(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // By rune, so a character outside the basic plane is kept or dropped whole, and so every control, format
        // (direction, zero-width, Unicode tag characters) and separator character goes.
        var text = string.Concat(value.EnumerateRunes().Where(rune => !IsHidden(rune))).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= MaxText)
        {
            return text;
        }

        var cut = char.IsHighSurrogate(text[MaxText - 1]) ? MaxText - 1 : MaxText;
        return text[..cut] + "…";
    }

    private static bool IsHidden(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static Uri? Link(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
}
