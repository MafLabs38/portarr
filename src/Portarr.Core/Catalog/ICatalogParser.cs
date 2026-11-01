namespace Portarr.Core.Catalog;

/// <summary>Reads a catalog with the connector sections the registered connector libraries own (D-105).</summary>
public interface ICatalogParser
{
    CatalogParseResult Parse(string yaml);
}
