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

    [Theory]
    [InlineData(" http://127.0.0.1:2375")]
    [InlineData("\thttp://169.254.169.254/")]
    [InlineData("tcp://127.0.0.1:2375 ")]
    public void should_read_an_address_the_way_the_connectors_do_whitespace_included(string text)
    {
        Check("docker", new JsonObject { ["url"] = text }).Should().ContainSingle().Which.Should().Contain("loopback, link-local");
    }

    [Fact]
    public void should_look_into_arrays_and_nested_settings()
    {
        var value = new JsonObject { ["nodes"] = new JsonArray(new JsonObject { ["url"] = "http://169.254.169.254/" }) };

        Check("kubernetes", value).Should().ContainSingle().Which.Should().StartWith("kubernetes.nodes[0].url");
    }

    [Theory]
    [InlineData("http://user:secret@10.0.0.7/ping", "user or password")]
    [InlineData("http://localhost:8484/", "metadata service")]
    [InlineData("http://app.localhost/", "metadata service")]
    [InlineData("http://metadata.google.internal/", "metadata service")]
    [InlineData("http://[::ffff:127.0.0.1]/", "loopback, link-local")]
    public void should_refuse_what_a_connector_must_never_call(string url, string reason)
    {
        Check("docker", new JsonObject { ["url"] = url }).Should().ContainSingle().Which.Should().Contain(reason);
    }

    [Theory]
    [InlineData("10.0.0.7")]
    [InlineData("server-1.homelab.example")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void should_accept_an_environment_address_that_is_a_host_or_an_address(string address)
    {
        ConnectionGuardrails.Check(new JsonObject { ["address"] = address }, ["address"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("[::1]")]
    [InlineData("localhost.")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("user@10.0.0.7")]
    [InlineData("10.0.0.7:8989")]
    [InlineData("10.0.0.7/path")]
    public void should_refuse_an_environment_address_that_is_not_a_host_or_means_this_host(string address)
    {
        ConnectionGuardrails.Check(new JsonObject { ["address"] = address }, ["address"]).Should().NotBeEmpty();
    }

    [Fact]
    public void should_refuse_a_scheme_a_connector_does_not_speak()
    {
        Check("docker", new JsonObject { ["url"] = "ftp://10.0.0.10/" }).Should().ContainSingle().Which.Should().Contain("scheme");
    }
}
