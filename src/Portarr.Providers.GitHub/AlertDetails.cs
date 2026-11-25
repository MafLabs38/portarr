using System.Globalization;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.GitHub;

/// <summary>
/// Reads the alerts GitHub returns as <see cref="AlertDetail"/>: what is wrong, where, and which version mends it.
/// Text comes from third parties (an advisory's summary): control and direction-changing characters are dropped and it
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
        var text = value is null ? null : new string([.. value.Where(c => !char.IsControl(c) && !IsBidiControl(c))]).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= MaxText ? text : text[..MaxText] + "…";
    }

    // Characters that reorder, hide or split text: bidi embeddings and overrides (U+202A–202E, U+2066–2069), marks
    // and zero-width characters (U+061C, U+200B–200F, U+2060, U+FEFF) and the line and paragraph separators.
    private static bool IsBidiControl(char c) =>
        c is '\u061C' or '\u2028' or '\u2029' or '\u2060' or '\uFEFF'
            or >= '\u200B' and <= '\u200F'
            or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069';

    private static Uri? Link(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
}
