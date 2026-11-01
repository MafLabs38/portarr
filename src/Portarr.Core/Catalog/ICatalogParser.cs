namespace Portarr.Core.Catalog;

/// <summary>Reads a catalog with the connector sections the registered connector libraries own (D-105).</summary>
public interface ICatalogParser
{
    CatalogParseResult Parse(string yaml);

    /// <summary>The environment sections the registered connectors own (<c>docker</c>, <c>kubernetes</c>…).</summary>
    IReadOnlyList<string> GetConnectorSections();
}
