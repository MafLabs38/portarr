namespace Portarr.Providers.Kubernetes;

/// <summary>Which namespaces a backup source reads: those the catalog's references (<c>namespace/name</c>) name, else the whole cluster.</summary>
internal static class KubernetesScopes
{
    /// <summary>The namespaces of the references, once each; a single <c>null</c> (the whole cluster) when none is given.</summary>
    public static IReadOnlyList<string?> Of(IReadOnlyCollection<string> references)
    {
        var namespaces = references
            .Select(reference => reference.Split('/', 2))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .Select(parts => parts[0])
            .Distinct(StringComparer.Ordinal)
            .Cast<string?>()
            .ToList();
        return namespaces.Count == 0 ? [null] : namespaces;
    }

    /// <summary>The list path of a resource, in a namespace or cluster-wide: <c>apis/batch/v1/namespaces/shop/cronjobs</c>.</summary>
    public static string Path(string? scope, string apiGroup, string resource) =>
        scope is null ? $"{apiGroup}/{resource}" : $"{apiGroup}/namespaces/{Uri.EscapeDataString(scope)}/{resource}";
}
