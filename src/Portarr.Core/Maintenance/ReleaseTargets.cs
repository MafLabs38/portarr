using Portarr.Core.Catalog;

namespace Portarr.Core.Maintenance;

/// <summary>
/// Which repository's latest release is read for each repository the operator makes (D-132): the repository itself when it publishes a release
/// (not a bare tag), or the one the catalog maps it to (<c>sources.github.releases</c>: a private source repository whose builds are published in
/// another, public one). The one place both the monitor (what to ask the scanner) and the report (what to show) agree on.
/// </summary>
public sealed class ReleaseTargets
{
    private const string GitHubHost = "github.com";

    private readonly Dictionary<string, string> _mapping;
    private readonly HashSet<string> _targets;

    private ReleaseTargets(Dictionary<string, string> mapping)
    {
        _mapping = mapping;
        _targets = new HashSet<string>(mapping.Values, StringComparer.OrdinalIgnoreCase);
    }

    public static ReleaseTargets None { get; } = new([]);

    /// <summary>The mappings of every GitHub source of the catalog, by source repository (case-insensitive).</summary>
    public static ReleaseTargets Of(Catalog.Catalog catalog)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.CodeSources.OfType<GitHubSource>().SelectMany(source => source.ReleaseRepositories))
        {
            mapping[entry.Key] = entry.Value;
        }

        return mapping.Count == 0 ? None : new ReleaseTargets(mapping);
    }

    /// <summary>
    /// The repository (<c>owner/name</c>) whose latest release is read for <paramref name="repository"/>, or null when none is. An archived repository
    /// or a fork has none, nor has a repository that is not on github.com (the companion reads github.com only: a GitHub Enterprise Server's
    /// <c>owner/name</c> could be another project's there); a mapped one reads the repository it is mapped to, whether it publishes a release itself or
    /// not; a repository that is the target of a mapping shows on its source, not for itself; a private repository that is not mapped has none (its
    /// release is not readable anonymously, and its name stays off the companion).
    /// </summary>
    public string? TargetOf(CodeRepository repository)
    {
        if (repository.Archived || repository.Fork || !string.Equals(repository.Url.Host, GitHubHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_mapping.TryGetValue(repository.FullName, out var mapped))
        {
            return mapped;
        }

        if (_targets.Contains(repository.FullName))
        {
            return null;
        }

        return repository.Release is { IsTagOnly: false } && string.Equals(repository.Visibility, "public", StringComparison.OrdinalIgnoreCase) ? repository.FullName : null;
    }

    /// <summary>The repositories whose latest release is to be scanned: one scan target of kind release each, once, in a stable order.</summary>
    public IReadOnlyList<ScanTarget> ScanTargets(IEnumerable<CodeRepository> repositories) =>
    [
        .. repositories
            .Select(TargetOf)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => new ScanTarget(ScanTargetKinds.Release, name)),
    ];
}
