using System.Text.RegularExpressions;

namespace Portarr.Core.Catalog;

public static partial class IconNames
{
    /// <summary>
    /// Guesses an icon name from a container image: <c>lscr.io/linuxserver/sonarr:4.0</c> → <c>sonarr</c>,
    /// <c>postgres:17-alpine</c> → <c>postgres</c>, <c>grafana/grafana@sha256:…</c> → <c>grafana</c>.
    /// </summary>
    public static string? FromImage(string image)
    {
        var withoutDigest = image.Split('@')[0];
        var repository = withoutDigest[(withoutDigest.LastIndexOf('/') + 1)..];
        var name = repository.Split(':')[0].ToLowerInvariant();

        return IsValid(name) ? name : null;
    }

    /// <summary>Icon names become file names and URL segments: letters, digits and dashes only.</summary>
    public static bool IsValid(string name)
    {
        return IconNamePattern().IsMatch(name);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IconNamePattern();
}
