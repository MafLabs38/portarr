using System.Text.Json.Nodes;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Core.Test.Catalog;

/// <summary>What a change to how a host is reached may not contain, without the editor, the store or the file system (D-123).</summary>
public sealed class ConnectionGuardrailsFixture
{
    private static IReadOnlyList<string> Check(string key, JsonNode value) => ConnectionGuardrails.Check(new JsonObject { [key] = value }, [key]);

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("tcp://10.0.0.10:2375")]
    [InlineData("http://socket-proxy:2375")]
    [InlineData("https://10.0.0.30:8006")]
    public void should_accept_an_address_of_a_tool_on_the_network(string url)
    {
        Check("docker", new JsonObject { ["url"] = url }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("C:\\tools\\mcp.exe")]
    [InlineData("--port=9222")]
    [InlineData("node")]
    public void should_leave_a_string_that_is_not_an_address_alone(string text)
    {
        Check("stdio", new JsonObject { ["command"] = text }).Should().BeEmpty();
    }

    [Fact]
    public void should_look_into_arrays_and_nested_settings()
    {
        var value = new JsonObject { ["nodes"] = new JsonArray(new JsonObject { ["url"] = "http://169.254.169.254/" }) };

        Check("kubernetes", value).Should().ContainSingle().Which.Should().StartWith("kubernetes.nodes[0].url");
    }

    [Fact]
    public void should_refuse_a_scheme_a_connector_does_not_speak()
    {
        Check("docker", new JsonObject { ["url"] = "ftp://10.0.0.10/" }).Should().ContainSingle().Which.Should().Contain("scheme");
    }
}
