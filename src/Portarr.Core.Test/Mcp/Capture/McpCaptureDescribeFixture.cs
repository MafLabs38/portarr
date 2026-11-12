using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Capture;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp.Capture;

/// <summary>The comparison with the catalog and the definition that accepting a captured server writes (V9, D-116).</summary>
public sealed class McpCaptureDescribeFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            name: Memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers: { X-Api-Key: { env: MEMORY_MCP_KEY }, Accept: application/json }
            notes: Long-term memory.
            check: { interval: 10m }
          - id: github
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "@example/github-mcp@1.4.2"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN } } }
        """;

    private static IReadOnlyList<McpServerDefinition> Servers => TestCatalog.Parse(Catalog).Catalog.McpServerList;

    private static CapturedServer Remote(params CapturedValue[] headers) => new("Memory", "http", "https://memory.homelab.example/mcp", null, [], headers, []);

    private static CapturedServer Local(string[] args, params CapturedValue[] env) => new("GitHub", "stdio", null, "npx", args, [], env);

    private static readonly Dictionary<string, string> NoVariables = [];

    [Fact]
    public void should_find_nothing_different_when_the_values_are_redacted_or_the_same()
    {
        var seen = Remote(new CapturedValue("X-Api-Key", null, null), new CapturedValue("Accept", null, "application/json"));

        McpCaptureService.Compare(Servers[0], seen).Should().BeEmpty();
        McpCaptureService.Compare(Servers[0], Remote(new CapturedValue("X-Api-Key", "MEMORY_MCP_KEY", null), new CapturedValue("Accept", null, "application/json"))).Should().BeEmpty();
    }

    [Fact]
    public void should_say_what_differs_in_the_headers()
    {
        var seen = Remote(
            new CapturedValue("X-Api-Key", "OTHER_KEY", null),
            new CapturedValue("Accept", null, "text/plain"),
            new CapturedValue("X-Team", null, "platform"));

        McpCaptureService.Compare(Servers[0], seen).Should().BeEquivalentTo(
            "header X-Api-Key: variable MEMORY_MCP_KEY → OTHER_KEY",
            "header Accept: the literal changed",
            "header X-Team: not in the catalog");
    }

    [Fact]
    public void should_say_a_header_is_missing_or_changed_from_a_literal_to_a_variable_and_back()
    {
        McpCaptureService.Compare(Servers[0], Remote(new CapturedValue("Accept", "ACCEPT_VAR", null))).Should().BeEquivalentTo(
            "header Accept: a literal in the catalog, the variable ACCEPT_VAR captured",
            "header X-Api-Key: in the catalog, not captured");
        McpCaptureService.Compare(Servers[0], Remote(new CapturedValue("X-Api-Key", null, "plain"), new CapturedValue("Accept", null, "application/json"))).Should().ContainSingle()
            .Which.Should().Be("header X-Api-Key: the variable MEMORY_MCP_KEY in the catalog, a literal captured");
    }

    [Fact]
    public void should_say_what_differs_in_the_command_and_its_arguments()
    {
        var seen = Local(["-y", "@example/github-mcp@2.0.0"], new CapturedValue("GITHUB_TOKEN", null, null)) with { Command = "npx.cmd" };

        McpCaptureService.Compare(Servers[1], seen).Should().BeEquivalentTo(
            "command: npx → npx.cmd",
            "args: -y @example/github-mcp@1.4.2 → -y @example/github-mcp@2.0.0");
    }

    [Fact]
    public void should_write_a_new_remote_definition_with_the_variables_that_were_named_or_proposed()
    {
        var seen = new CapturedServer(
            "Search Tool",
            "http",
            "https://search.homelab.example/mcp",
            null,
            [],
            [new CapturedValue("X-Api-Key", null, null), new CapturedValue("Accept", null, "application/json")],
            []);
        var variables = new Dictionary<string, string> { ["header:X-Api-Key"] = "SEARCH_TOOL_X_API_KEY" };

        var item = McpCaptureService.BuildItem(seen, "search-tool", "Search Tool", "user", variables, null);

        item.ToJsonString().Should().Be("""{"id":"search-tool","name":"Search Tool","transport":"http","http":{"url":"https://search.homelab.example/mcp","headers":{"Accept":"application/json","X-Api-Key":{"env":"SEARCH_TOOL_X_API_KEY"}}}}""");
    }

    [Fact]
    public void should_omit_the_name_when_it_is_the_id_keep_the_scope_when_it_is_not_user_and_allow_a_plain_http_address_the_operator_reviews()
    {
        var seen = new CapturedServer("lan", "http", "http://10.0.0.30:8080/mcp", null, [], [], []);

        var item = McpCaptureService.BuildItem(seen, "lan", "lan", "project", NoVariables, null);

        item.ToJsonString().Should().Be("""{"id":"lan","transport":"http","http":{"url":"http://10.0.0.30:8080/mcp","allowInsecure":true},"scope":"project"}""");
    }

    [Fact]
    public void should_write_a_local_definition_without_any_secret_value()
    {
        var seen = Local(["-y", "@example/github-mcp@2.0.0"], new CapturedValue("GITHUB_TOKEN", "GITHUB_TOKEN", null), new CapturedValue("LOG_LEVEL", null, "info"));

        var item = McpCaptureService.BuildItem(seen, "github", "GitHub", "user", new Dictionary<string, string> { ["env:GITHUB_TOKEN"] = "GITHUB_TOKEN" }, null);

        item.ToJsonString().Should().Be("""{"id":"github","name":"GitHub","transport":"stdio","stdio":{"command":"npx","args":["-y","@example/github-mcp@2.0.0"],"env":{"GITHUB_TOKEN":{"env":"GITHUB_TOKEN"},"LOG_LEVEL":"info"}}}""");
    }

    [Fact]
    public void should_replace_a_definition_and_keep_its_notes_check_scope_and_the_catalogs_own_name()
    {
        var current = new JsonObject
        {
            ["id"] = "memory",
            ["name"] = "Memory",
            ["transport"] = "http",
            ["http"] = new JsonObject { ["url"] = "https://memory.homelab.example/mcp", ["headers"] = new JsonObject { ["X-Old"] = "x" } },
            ["notes"] = "Long-term memory.",
            ["check"] = new JsonObject { ["interval"] = "10m" },
        };
        var seen = Remote(new CapturedValue("X-Api-Key", "MEMORY_MCP_KEY", null));

        var item = McpCaptureService.BuildItem(seen, "memory", "Memory", "project", new Dictionary<string, string> { ["header:X-Api-Key"] = "MEMORY_MCP_KEY" }, current);

        item.ToJsonString().Should().Be("""{"id":"memory","name":"Memory","transport":"http","http":{"url":"https://memory.homelab.example/mcp","headers":{"X-Api-Key":{"env":"MEMORY_MCP_KEY"}}},"notes":"Long-term memory.","check":{"interval":"10m"}}""");
    }

    [Fact]
    public void should_turn_a_remote_definition_into_a_local_one_when_the_catalog_entry_is_replaced_by_a_local_server()
    {
        var current = new JsonObject { ["id"] = "x", ["transport"] = "http", ["http"] = new JsonObject { ["url"] = "https://a.homelab.example/mcp" }, ["notes"] = "n" };

        var item = McpCaptureService.BuildItem(Local(["-y", "pkg"]), "x", "x", "user", NoVariables, current);

        item.ToJsonString().Should().Be("""{"id":"x","transport":"stdio","notes":"n","stdio":{"command":"npx","args":["-y","pkg"]}}""");
    }

    [Theory]
    [InlineData("Search Tool", "search-tool")]
    [InlineData("  n8n workflow builder!! ", "n8n-workflow-builder")]
    [InlineData("---", "server")]
    public void should_make_an_id_from_a_name(string name, string expected)
    {
        McpCaptureService.Slug(name, []).Should().Be(expected);
    }

    [Fact]
    public void should_make_an_id_unique_among_the_catalogs()
    {
        McpCaptureService.Slug("memory", ["memory", "memory-2"]).Should().Be("memory-3");
    }

    [Theory]
    [InlineData("memory-x-api-key", "MEMORY_X_API_KEY")]
    [InlineData("1st.key", "_1ST_KEY")]
    public void should_make_a_variable_name_from_any_text(string text, string expected)
    {
        McpCaptureService.VariableName(text).Should().Be(expected);
    }

    [Fact]
    public void should_write_the_yaml_to_paste_into_a_catalog_that_cannot_be_written()
    {
        var seen = new CapturedServer("memory", "http", "https://memory.homelab.example/mcp", null, [], [new CapturedValue("X-Api-Key", "MEMORY_MCP_KEY", null)], []);

        var yaml = McpCaptureService.ProposalYaml(McpCaptureService.BuildItem(seen, "memory", "memory", "user", new Dictionary<string, string> { ["header:X-Api-Key"] = "MEMORY_MCP_KEY" }, null));

        TestCatalog.Parse(yaml).IsValid.Should().BeTrue(string.Join("; ", TestCatalog.Parse(yaml).Errors));
        TestCatalog.Parse(yaml).Catalog.McpServerList.Should().ContainSingle().Which.Http!.Headers["X-Api-Key"].EnvName.Should().Be("MEMORY_MCP_KEY");
        yaml.Should().StartWith("mcpServers:\n  - ");
    }

    private static JsonObject Workstation(string clients, string mcp) => (JsonObject)JsonNode.Parse($$"""{"id":"pc","os":"linux","clients":{{clients}},"mcp":{{mcp}}}""")!;

    [Fact]
    public void should_want_the_server_in_every_client_with_a_bare_id()
    {
        var result = McpCaptureService.WithWanted(Workstation("[\"claude-code\"]", "[]"), "memory", ["claude-code"]);

        result["mcp"]!.ToJsonString().Should().Be("""["memory"]""");
        result["clients"]!.ToJsonString().Should().Be("""["claude-code"]""");
    }

    [Fact]
    public void should_name_the_clients_of_the_others_before_adding_a_client_so_that_nothing_is_silently_wanted_there()
    {
        var result = McpCaptureService.WithWanted(Workstation("[\"claude-code\"]", "[\"memory\"]"), "github", ["cursor"]);

        result["clients"]!.ToJsonString().Should().Be("""["claude-code","cursor"]""");
        result["mcp"]!.ToJsonString().Should().Be("""[{"server":"memory","clients":["claude-code"]},{"server":"github","clients":["cursor"]}]""");
    }

    [Fact]
    public void should_add_a_client_to_a_server_that_is_already_wanted_and_go_back_to_a_bare_id_when_every_client_wants_it()
    {
        var one = Workstation("[\"claude-code\",\"cursor\"]", "[{\"server\":\"memory\",\"clients\":[\"claude-code\"]}]");

        var result = McpCaptureService.WithWanted(one, "memory", ["cursor"]);

        result["mcp"]!.ToJsonString().Should().Be("""["memory"]""");
    }

    [Fact]
    public void should_keep_the_scope_of_an_entry_that_has_one_and_leave_the_other_entries_alone()
    {
        var one = Workstation("[\"claude-code\",\"cursor\"]", "[\"search\",{\"server\":\"memory\",\"clients\":[\"claude-code\"],\"scope\":\"project\"}]");

        var result = McpCaptureService.WithWanted(one, "memory", ["cursor"]);

        result["mcp"]!.ToJsonString().Should().Be("""["search",{"server":"memory","clients":["claude-code","cursor"],"scope":"project"}]""");
    }
}
