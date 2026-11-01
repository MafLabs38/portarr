using System.Reflection;

namespace Portarr.Core.Catalog;

/// <summary>
/// The connectors a catalog is read with. Two connectors cannot own the same section, and a connector cannot own a key that
/// Core still reads itself: either would silently take a key away from Core's strict reading (D-105).
/// </summary>
internal static class EnvironmentConnectorRegistry
{
    private static readonly HashSet<string> ReservedKeys = typeof(EnvironmentDocument)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.Name != nameof(EnvironmentDocument.Sections))
        .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<IEnvironmentConnector> Validate(IEnumerable<IEnvironmentConnector> connectors)
    {
        var registered = connectors.OrderBy(c => c.Section, StringComparer.Ordinal).ToList();
        if (registered.FirstOrDefault(c => ReservedKeys.Contains(c.Section)) is { } reserved)
        {
            throw new InvalidOperationException($"The connector {reserved.GetType().Name} owns the section '{reserved.Section}', which Core still reads itself.");
        }

        var twice = registered.GroupBy(c => c.Section, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (twice is not null)
        {
            throw new InvalidOperationException($"The section '{twice.Key}' is owned by two connectors: {string.Join(", ", twice.Select(c => c.GetType().Name))}.");
        }

        return registered;
    }
}
