namespace Portarr.Core.Catalog;

/// <summary>Reads a catalog with the connector sections the registered connector libraries own (D-105).</summary>
public interface ICatalogParser
{
    CatalogParseResult Parse(string yaml);
}

public sealed class CatalogParserService : ICatalogParser
{
    private readonly IReadOnlyList<IEnvironmentConnector> _connectors;

    public CatalogParserService(IEnumerable<IEnvironmentConnector> connectors)
    {
        _connectors = connectors.ToList();
    }

    public CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, _connectors);
}
