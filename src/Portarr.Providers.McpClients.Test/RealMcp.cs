using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Portarr.Test.Common;
using CatalogModel = Portarr.Core.Catalog.Catalog;

namespace Portarr.Providers.McpClients.Test;

/// <summary>The real adapters of the library, composed the way the host does, for the tests that read what they produce.</summary>
internal static class RealMcp
{
    public static IMcpClientRegistry Clients { get; } = new McpClientRegistry(McpClientsProvider.Renderers());

    public static IMcpSecretStoreRegistry Stores { get; } = new McpSecretStoreRegistry(McpClientsProvider.Provisioners());

    public static IMcpVocabulary Vocabulary { get; } = new McpVocabulary(Clients, Stores);

    /// <summary>A catalog read with the names the real adapters registered.</summary>
    public static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, TestCatalog.Connectors, null, Vocabulary);

    /// <summary>The rendering service over a catalog (its secret store is the one the catalog declares).</summary>
    public static IMcpRenderingService Rendering(CatalogModel catalog)
    {
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(catalog, "catalog.yaml", true, [], new FakeTimeProvider().GetUtcNow()));
        return new McpRenderingService(Clients, Stores, catalogService);
    }
}
