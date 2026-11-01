using Portarr.Core.Catalog;

namespace Portarr.Test.Common;

/// <summary>
/// A connector of the catalog that accepts its section whatever it holds: for tests of the generic parts of Core, which only
/// care that an environment declares the section (D-105). The real connector's validation is tested in its own project.
/// </summary>
public sealed class StubEnvironmentConnector : IEnvironmentConnector
{
    public StubEnvironmentConnector(string section)
    {
        Section = section;
    }

    public string Section { get; }

    public EnvironmentConnectorResult? Parse(EnvironmentSectionContext context) => new(new object());
}
