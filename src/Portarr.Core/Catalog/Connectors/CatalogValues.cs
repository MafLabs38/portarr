using System.Text.RegularExpressions;

namespace Portarr.Core.Catalog;

/// <summary>The checks the catalog applies to a value, shared with the connectors that read their own sections (D-105).</summary>
public static partial class CatalogValues
{
    /// <summary>The namespace of the variables that hold a reader's token: the only ones a catalog may ask Portarr to send to a URL (D-102).</summary>
    public const string ReaderTokenPrefix = "PORTARR_READER_";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex VariableNamePattern();

    /// <summary>The one duration grammar of the catalog: a whole number and a unit, <c>30s</c>, <c>5m</c>, <c>24h</c>, <c>7d</c>; a bare number is seconds.</summary>
    [GeneratedRegex("^(?<value>[0-9]{1,6})(?<unit>[smhd]?)$")]
    private static partial Regex DurationPattern();

    /// <summary>A Kubernetes namespace: a DNS label.</summary>
    [GeneratedRegex("^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")]
    private static partial Regex NamespacePattern();

    /// <summary>A Kubernetes label key: a name of at most 63 characters, with an optional DNS-subdomain prefix and a slash.</summary>
    [GeneratedRegex("^([a-z0-9]([-a-z0-9.]{0,251}[a-z0-9])?/)?[A-Za-z0-9]([-A-Za-z0-9_.]{0,61}[A-Za-z0-9])?$")]
    private static partial Regex LabelKeyPattern();

    /// <summary>A Kubernetes label value: at most 63 characters of letters, digits, dashes, underscores and dots, starting and ending with a letter or digit.</summary>
    [GeneratedRegex("^[A-Za-z0-9]([-A-Za-z0-9_.]{0,61}[A-Za-z0-9])?$")]
    private static partial Regex LabelValuePattern();

    // Must match ReaderTokenPrefix.
    [GeneratedRegex("^PORTARR_READER_[A-Za-z0-9_]{1,100}$")]
    private static partial Regex ReaderTokenVariablePattern();

    /// <summary>Null when the value is missing or blank, else the value trimmed.</summary>
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An http(s) URL without credentials: read with GET only, so it must not carry a secret.</summary>
    public static Uri? HttpUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 ? url : null;
    }

    /// <summary>A variable of the reader namespace: <c>PORTARR_READER_</c> followed by letters, digits and underscores.</summary>
    public static bool IsReaderTokenVariable(string value) => ReaderTokenVariablePattern().IsMatch(value);

    /// <summary>
    /// The optional <c>tokenEnv</c> of a section that sends a bearer token to a catalog URL (D-102): absent is fine, present must
    /// be in the reader namespace. Returns the error to report, or null.
    /// </summary>
    public static string? ReaderTokenError(string key, string? value, out string? variable)
    {
        variable = Blank(value);
        if (variable is null || IsReaderTokenVariable(variable))
        {
            return null;
        }

        return $"{key} must be the name of an environment variable starting with {ReaderTokenPrefix} (e.g. {ReaderTokenPrefix}SERVER_1), never the token itself.";
    }

    /// <summary>
    /// A duration in the catalog's one grammar (<c>5</c> or <c>5s</c> seconds, <c>2m</c>, <c>24h</c>, <c>7d</c>), between
    /// <paramref name="min"/> and <paramref name="max"/>; null when the text is none of these or out of bounds.
    /// </summary>
    public static TimeSpan? ParseDuration(string? value, TimeSpan min, TimeSpan max)
    {
        var match = DurationPattern().Match(value?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var amount = int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var duration = match.Groups["unit"].Value switch
        {
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            "d" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromSeconds(amount),
        };
        return duration >= min && duration <= max ? duration : null;
    }

    /// <summary>A Kubernetes label key (<c>app</c>, <c>app.kubernetes.io/name</c>).</summary>
    public static bool IsLabelKey(string value) => LabelKeyPattern().IsMatch(value);

    /// <summary>A Kubernetes label value (<c>shop-api</c>).</summary>
    public static bool IsLabelValue(string value) => LabelValuePattern().IsMatch(value);

    /// <summary>A Kubernetes namespace name: a DNS label.</summary>
    public static bool IsNamespaceName(string value) => NamespacePattern().IsMatch(value);

    /// <summary>The name of an environment variable (the catalog names the variable that holds a secret, never the secret).</summary>
    public static bool IsVariableName(string value) => VariableNamePattern().IsMatch(value);
}
