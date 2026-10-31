namespace Portarr.Core.Catalog;

/// <summary>
/// The GitHub accounts whose repositories the Maintenance page scans (D-64): the apps the operator makes,
/// deployed or not (a desktop app, a utility, a library). Declared under <c>sources: github:</c>.
/// </summary>
/// <param name="Owners">User or organization logins to scan.</param>
/// <param name="TokenVariable">Name of the environment variable holding a read-only token, never the token itself.</param>
/// <param name="PushedWithinMonths">Only repositories pushed in the last N months; 0 scans them all.</param>
/// <param name="Include">Repositories (<c>owner/name</c>) always scanned, even when inactive or not owned by an owner above.</param>
/// <param name="Exclude">Repositories never scanned: <c>owner/name</c>, with <c>*</c> as a wildcard (<c>Maf/test-*</c>).</param>
/// <param name="Forks">Whether forks are scanned.</param>
/// <param name="Archived">Whether archived repositories are scanned.</param>
public sealed record GitHubSource(
    IReadOnlyList<string> Owners,
    string TokenVariable,
    int PushedWithinMonths,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    bool Forks,
    bool Archived)
{
    public const string DefaultTokenVariable = "GITHUB_TOKEN";
    public const int DefaultPushedWithinMonths = 6;

    /// <summary>Whether a repository (<c>owner/name</c>) is excluded; case-insensitive like GitHub names.</summary>
    public bool IsExcluded(string fullName)
    {
        return Exclude.Any(pattern => Matches(pattern, fullName));
    }

    private static bool Matches(string pattern, string value)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        }

        if (!value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || !value.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = parts[0].Length;
        foreach (var part in parts[1..^1])
        {
            var found = value.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return position <= value.Length - parts[^1].Length;
    }
}
