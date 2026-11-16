using Portarr.Core.Catalog;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>
/// The <c>machine</c> runtime (D-126): <c>runtime: machine</c> is the whole declaration. The section has no setting, so it cannot
/// join the connector contract (which extends a section with an unknown key): these tests say the same things by hand.
/// </summary>
public sealed class MachineEnvironmentConnectorFixture
{
    private static CatalogParseResult Parse(string environment) => CatalogParser.Parse($"environments:\n  - id: server-1\n{environment}", [new MachineEnvironmentConnector()]);

    [Fact]
    public void should_declare_a_machine_with_the_runtime_alone()
    {
        var parsed = Parse("    runtime: machine\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().IsMachineOnly().Should().BeTrue();
        parsed.Catalog.Environments.Single().Runtime.Should().Be("machine");
    }

    [Fact]
    public void should_refuse_a_key_in_the_section_naming_its_line()
    {
        var parsed = Parse("    runtime: machine\n    machine: { unexpectedKey: 1 }\n");

        parsed.Errors.Should().ContainSingle().Which.Should().StartWith("Environment 'server-1': machine: Invalid YAML at line 4").And.Contain("unexpectedKey");
    }

    [Fact]
    public void should_leave_an_empty_section_undeclared_without_an_error()
    {
        var parsed = Parse("    machine:\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments.Single().IsMachineOnly().Should().BeFalse();
    }
}
