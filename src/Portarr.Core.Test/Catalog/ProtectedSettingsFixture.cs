using System.Text.Json.Nodes;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Core.Test.Catalog;

/// <summary>How hosts are reached (connector sections, runtime, reader, check) is never changed by an edit (17-1).</summary>
public sealed class ProtectedSettingsFixture
{
    private static readonly string[] Connectors = ["docker", "kubernetes", "cloudflare", "backrest", "hostMetrics"];

    private static IReadOnlyList<string> EnvironmentKeys => ProtectedSettings.KeysOf(CatalogSections.Environments, Connectors);

    private static JsonObject Environment() => JsonNode.Parse(
        """{"id":"server-1","name":"Server 1","docker":{"url":"http://socket-proxy:2375","tokenEnv":"PORTARR_READER_TOKEN"},"notes":"old"}""")!.AsObject();

    [Fact]
    public void should_protect_the_runtime_the_reader_and_every_connector_section_of_an_environment_and_the_check_of_an_app()
    {
        EnvironmentKeys.Should().BeEquivalentTo(["runtime", "reader", .. Connectors]);
        ProtectedSettings.KeysOf(CatalogSections.Apps, Connectors).Should().BeEquivalentTo(["check"]);
        ProtectedSettings.KeysOf(CatalogSections.Stacks, Connectors).Should().BeEmpty();
        ProtectedSettings.KeysOf(CatalogSections.Providers, Connectors).Should().BeEmpty();
    }

    [Fact]
    public void should_keep_the_current_connector_settings_when_an_edit_leaves_them_out()
    {
        var edited = JsonNode.Parse("""{"id":"server-1","name":"Renamed"}""")!.AsObject();

        var result = ProtectedSettings.Apply(CatalogSections.Environments, "server-1", Environment(), edited, EnvironmentKeys)!;

        result["name"]!.ToString().Should().Be("Renamed");
        result["docker"]!["url"]!.ToString().Should().Be("http://socket-proxy:2375");
        edited.ContainsKey("docker").Should().BeFalse("the caller's object is not changed");
    }

    [Fact]
    public void should_accept_an_edit_that_sends_the_settings_back_unchanged()
    {
        var edited = Environment();
        edited["notes"] = "new";

        var result = ProtectedSettings.Apply(CatalogSections.Environments, "server-1", Environment(), edited, EnvironmentKeys)!;

        result["notes"]!.ToString().Should().Be("new");
    }

    [Theory]
    [InlineData("""{"id":"server-1","docker":{"url":"http://attacker.example:2375","tokenEnv":"PORTARR_READER_TOKEN"}}""", "docker")]
    [InlineData("""{"id":"server-1","docker":{"url":"http://socket-proxy:2375","tokenEnv":"PORTARR_READER_TOKEN"},"kubernetes":{"server":"https://attacker.example"}}""", "kubernetes")]
    [InlineData("""{"id":"server-1","runtime":"none","docker":{"url":"http://socket-proxy:2375","tokenEnv":"PORTARR_READER_TOKEN"}}""", "runtime")]
    public void should_refuse_an_edit_that_sets_or_changes_one(string sent, string key)
    {
        var edit = () => ProtectedSettings.Apply(CatalogSections.Environments, "server-1", Environment(), JsonNode.Parse(sent)!.AsObject(), EnvironmentKeys);

        edit.Should().Throw<CatalogEditException>().Which.Message.Should().Contain(key).And.Contain("catalog file");
    }

    [Fact]
    public void should_refuse_a_new_environment_that_brings_its_own_connector_settings()
    {
        var edit = () => ProtectedSettings.Apply(CatalogSections.Environments, "elsewhere", null, JsonNode.Parse("""{"id":"elsewhere","docker":{"url":"http://attacker.example:2375"}}""")!.AsObject(), EnvironmentKeys);

        edit.Should().Throw<CatalogEditException>();
    }

    [Fact]
    public void should_let_an_item_be_removed_whole()
    {
        ProtectedSettings.Apply(CatalogSections.Environments, "server-1", Environment(), null, EnvironmentKeys).Should().BeNull();
    }

    [Fact]
    public void should_leave_out_the_protected_settings_of_what_an_assistant_reads()
    {
        var redacted = ProtectedSettings.Redact(Environment(), EnvironmentKeys);

        redacted.ContainsKey("docker").Should().BeFalse();
        redacted["name"]!.ToString().Should().Be("Server 1");
    }
}
