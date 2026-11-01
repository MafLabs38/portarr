namespace Portarr.Core.Catalog;

/// <summary>The catalog parser the application uses: the registered connectors, checked once at startup.</summary>
public sealed class CatalogParserService : ICatalogParser
{
    private readonly IReadOnlyList<IEnvironmentConnector> _connectors;

    public CatalogParserService(IEnumerable<IEnvironmentConnector> connectors)
    {
        _connectors = EnvironmentConnectorRegistry.Validate(connectors);
    }

    public IReadOnlyList<string> ConnectorSections => _connectors.Select(c => c.Section).ToList();

    public CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, _connectors);
}
