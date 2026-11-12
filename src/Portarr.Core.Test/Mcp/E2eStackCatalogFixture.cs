using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

/// <summary>
/// The catalog of the end-to-end stack (tests/e2e/stack) feeds the Playwright journey of the MCP page (V9). It is not parsed by anything in the
/// unit tests, so a typo would only show up in a CI run that builds an image: this reads the MCP part of it with the real parser.
/// </summary>
public sealed class E2eStackCatalogFixture
{
    private static string Read()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "tests", "e2e", "stack", "catalog.yaml");
            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
        }

        throw new InvalidOperationException("tests/e2e/stack/catalog.yaml was not found above the test binaries.");
    }

    [Fact]
    public void should_declare_the_servers_and_workstations_the_mcp_journey_walks()
    {
        var text = Read();
        var mcp = text[text.IndexOf("mcpServers:", StringComparison.Ordinal)..];

        var result = TestCatalog.Parse(mcp);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.McpServerList.Select(s => s.Id).Should().Equal("memory", "search", "github");
        result.Catalog.WorkstationList.Select(w => w.Id).Should().Equal("dev-laptop", "dev-vm");
    }
}
