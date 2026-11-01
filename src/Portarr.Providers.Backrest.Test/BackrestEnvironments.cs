using Portarr.Core.Catalog;

namespace Portarr.Providers.Backrest.Test;

internal static class BackrestEnvironments
{
    /// <summary>The settings bag of an environment that declares this Backrest, as the connector leaves it (D-105).</summary>
    public static IReadOnlyDictionary<string, object> Declared(BackrestEndpoint endpoint)
    {
        return new Dictionary<string, object> { [BackrestEnvironmentConnector.SectionName] = endpoint };
    }
}
