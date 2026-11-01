using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Catalog;

/// <summary>The catalog parser the application uses: the registered connectors, checked once at startup.</summary>
public sealed class CatalogParserService : ICatalogParser
{
    private readonly IReadOnlyList<IEnvironmentConnector> _connectors;
    private readonly IReadOnlySet<string> _allowedSecretVariables;

    public CatalogParserService(IEnumerable<IEnvironmentConnector> connectors, IOptions<PortarrOptions>? options = null)
    {
        _connectors = EnvironmentConnectorRegistry.Validate(connectors);
        _allowedSecretVariables = CatalogValues.ParseVariableList(options?.Value.Catalog.SecretVariables);
    }

    public IReadOnlyList<string> GetConnectorSections() => _connectors.Select(c => c.Section).ToList();

    public CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, _connectors, _allowedSecretVariables);
}
