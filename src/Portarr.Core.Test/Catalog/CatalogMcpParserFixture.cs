using System.Text.Json.Nodes;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogMcpParserFixture
{
    private const string Servers = """
        apps:
          - { id: memory, name: Memory, type: service }
        mcpServers:
          - id: memory
            name: Memory
            app: memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers:
                X-Api-Key: { env: MEMORY_MCP_KEY }
                Accept: application/json
            notes: Long-term memory.
          - id: github
            transport: stdio
            scope: project
            stdio:
              command: npx
              args: ["-y", "@example/github-mcp@1.4.2"]
              env: { GITHUB_TOKEN: { env: GITHUB_TOKEN }, LOG_LEVEL: info }
        """;

    private static IReadOnlyList<string> Errors(string yaml) => TestCatalog.Parse(yaml).Errors;

    private static string Http(string extra = "", string url = "https://memory.homelab.example/mcp")
    {
        return $"mcpServers:\n  - id: memory\n    transport: http\n    http:\n      url: {url}\n{extra}";
    }

    [Fact]
    public void should_read_a_remote_and_a_local_server()
    {
        var result = TestCatalog.Parse(Servers);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var servers = result.Catalog.McpServerList;
        servers.Should().HaveCount(2);

        var memory = servers[0];
        memory.Name.Should().Be("Memory");
        memory.Transport.Should().Be(McpTransports.Http);
        memory.Scope.Should().Be(McpScopes.User);
        memory.AppId.Should().Be("memory");
        memory.Notes.Should().Be("Long-term memory.");
        memory.Http!.Url.Should().Be(new Uri("https://memory.homelab.example/mcp"));
        memory.Http.Headers["X-Api-Key"].Should().Be(McpValue.FromEnv("MEMORY_MCP_KEY"));
        memory.Http.Headers["Accept"].Should().Be(McpValue.FromLiteral("application/json"));
        memory.Stdio.Should().BeNull();

        var github = servers[1];
        github.Name.Should().Be("github");
        github.Scope.Should().Be(McpScopes.Project);
        github.Stdio!.Command.Should().Be("npx");
        github.Stdio.Args.Should().Equal("-y", "@example/github-mcp@1.4.2");
        github.Stdio.Env["GITHUB_TOKEN"].IsEnv.Should().BeTrue();
        github.Stdio.Env["LOG_LEVEL"].Literal.Should().Be("info");
    }

    [Fact]
    public void should_have_no_server_without_the_section()
    {
        TestCatalog.Parse("apps:\n  - { id: blog, name: Blog }").Catalog.McpServerList.Should().BeEmpty();
    }

    [Fact]
    public void should_check_a_remote_server_every_five_minutes_and_not_a_local_one()
    {
        var servers = TestCatalog.Parse(Servers).Catalog.McpServerList;

        servers[0].CheckInterval.Should().Be(TimeSpan.FromMinutes(5));
        servers[0].CheckTimeout.Should().Be(TimeSpan.FromSeconds(10));
        servers[1].CheckInterval.Should().BeNull();
    }

    [Fact]
    public void should_read_the_interval_and_the_timeout_of_a_check()
    {
        var server = TestCatalog.Parse(Http("    check: { interval: 30m, timeout: 20s }\n")).Catalog.McpServerList.Single();

        server.CheckInterval.Should().Be(TimeSpan.FromMinutes(30));
        server.CheckTimeout.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void should_not_check_a_server_whose_check_is_false()
    {
        TestCatalog.Parse(Http("    check: false\n")).Catalog.McpServerList.Single().CheckInterval.Should().BeNull();
    }

    [Fact]
    public void should_accept_a_plain_http_address_only_when_it_is_allowed()
    {
        Errors(Http(url: "http://10.0.0.30:8080/mcp")).Should().ContainSingle().Which.Should().Contain("allowInsecure");
        Errors(Http("      allowInsecure: true\n", "http://10.0.0.30:8080/mcp")).Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_credential_written_as_a_literal()
    {
        var errors = Errors(Http("      headers:\n        Authorization: Bearer abc\n"));

        errors.Should().ContainSingle().Which.Should().Contain("http.headers.Authorization").And.Contain("{ env: NAME }");
    }

    [Fact]
    public void should_refuse_a_value_that_looks_like_a_secret_without_echoing_it()
    {
        var secret = "ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789";

        var errors = Errors(Http($"      headers:\n        X-Team: {secret}\n"));

        errors.Should().ContainSingle().Which.Should().Contain("http.headers.X-Team").And.Contain("secret").And.NotContain(secret);
    }

    [Fact]
    public void should_refuse_credentials_in_the_address()
    {
        Errors(Http(url: "https://user:pass@memory.homelab.example/mcp")).Should().ContainSingle().Which.Should().NotContain("pass@");
    }

    [Theory]
    [InlineData("transport: websocket", "transport must be http or stdio")]
    [InlineData("transport: stdio\n    http: { url: \"https://a.homelab.example/mcp\" }\n    stdio: { command: npx }", "a stdio server declares stdio, not http")]
    [InlineData("transport: http", "http.url is required")]
    [InlineData("transport: http\n    stdio: { command: npx }\n    http: { url: \"https://a.homelab.example/mcp\" }", "an http server declares http, not stdio")]
    [InlineData("transport: stdio", "stdio.command is required")]
    [InlineData("transport: stdio\n    stdio: { command: \"npx -y pkg && rm -rf /\" }", "not a shell line")]
    [InlineData("transport: stdio\n    scope: machine\n    stdio: { command: npx }", "scope must be")]
    [InlineData("transport: stdio\n    app: ghost\n    stdio: { command: npx }", "app 'ghost' is not an app")]
    [InlineData("transport: stdio\n    check: true\n    stdio: { command: npx }", "cannot reach")]
    [InlineData("transport: stdio\n    stdio: { command: npx, env: { 1BAD: { env: GOOD } } }", "invalid name")]
    [InlineData("transport: stdio\n    stdio: { command: npx, env: { TOKEN: { env: not-a-name } } }", "environment variable")]
    [InlineData("transport: stdio\n    stdio: { command: npx, env: { TOKEN: { env: \"GOOD_NAME\\n\" } } }", "environment variable")]
    [InlineData("transport: stdio\n    stdio: { command: npx, env: { LEVEL: {} } }", "needs a value")]
    [InlineData("transport: stdio\n    stdio: { command: cmd, args: [\"/c\", \"npx\", \"--opt=a&calc\"] }", "is read by cmd.exe")]
    [InlineData("transport: stdio\n    stdio: { command: CMD.EXE, args: [\"/c\", \"npx\", \"%PATH%\"] }", "is read by cmd.exe")]
    public void should_refuse_an_invalid_server(string body, string expected)
    {
        var errors = Errors($"mcpServers:\n  - id: memory\n    {body}");

        errors.Should().Contain(error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("check: { interval: 5s }", "check.interval")]
    [InlineData("check: { interval: 2d }", "check.interval")]
    [InlineData("check: { interval: 1m, timeout: 2m }", "check.timeout")]
    [InlineData("check: { url: \"https://a.homelab.example\" }", "only interval and timeout")]
    [InlineData("check: { keyword: ok }", "only interval and timeout")]
    [InlineData("check: maybe", "check must be false, true or a mapping")]
    public void should_refuse_an_invalid_check(string check, string expected)
    {
        Errors(Http($"    {check}\n")).Should().Contain(error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void should_refuse_the_same_id_twice()
    {
        var errors = Errors("""
            mcpServers:
              - { id: memory, transport: stdio, stdio: { command: npx } }
              - { id: memory, transport: stdio, stdio: { command: uvx } }
            """);

        errors.Should().ContainSingle().Which.Should().Contain("used twice");
    }

    [Fact]
    public void should_refuse_an_unknown_key_of_a_value()
    {
        // The strict reader rejects what the catalog does not know yet, instead of ignoring it.
        TestCatalog.Parse(Http("      headers:\n        X-Api-Key: { vault: memory }\n")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_keep_the_mcp_definitions_out_of_the_sections_an_api_key_or_the_chat_can_edit()
    {
        // D-112: a definition becomes a command on the operator's machines, so only a person edits it (the file, or the UI with a browser session).
        CatalogSections.All.Should().NotContain(["mcpServers", "workstations", "secretStore"]);
    }

    [Fact]
    public void should_leave_the_mcp_servers_untouched_when_an_app_is_edited()
    {
        var text = "# my catalog\napps:\n  - { id: blog, name: Blog }   # the blog\n\nmcpServers:\n  # memory\n  - id: memory\n    transport: http\n    http: { url: \"https://memory.homelab.example/mcp\" }   # remote\n";

        var edited = CatalogTextEditor.Apply(text, "apps", "blog", new JsonObject { ["id"] = "blog", ["name"] = "Blog 2" });

        edited.Text.Should().Contain("name: Blog 2");
        edited.Text.Should().EndWith("mcpServers:\n  # memory\n  - id: memory\n    transport: http\n    http: { url: \"https://memory.homelab.example/mcp\" }   # remote\n");
        TestCatalog.Parse(edited.Text).Catalog.McpServerList.Should().ContainSingle().Which.Id.Should().Be("memory");
    }
}
