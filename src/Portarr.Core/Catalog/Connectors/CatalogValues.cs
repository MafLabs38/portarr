using System.Text.RegularExpressions;

namespace Portarr.Core.Catalog;

/// <summary>The checks the catalog applies to a value, shared with the connectors that read their own sections (D-105).</summary>
public static partial class CatalogValues
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex VariableNamePattern();

    /// <summary>Null when the value is missing or blank, else the value trimmed.</summary>
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An http(s) URL without credentials: read with GET only, so it must not carry a secret.</summary>
    public static Uri? HttpUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 ? url : null;
    }

    /// <summary>The name of an environment variable (the catalog names the variable that holds a secret, never the secret).</summary>
    public static bool IsVariableName(string value) => VariableNamePattern().IsMatch(value);
}
