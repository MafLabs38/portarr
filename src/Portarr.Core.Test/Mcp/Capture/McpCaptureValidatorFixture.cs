using Portarr.Core.Mcp.Capture;

namespace Portarr.Core.Test.Mcp.Capture;

/// <summary>What an assistant may hand over (D-116): never a secret value, bounded, and what the catalog would accept.</summary>
public sealed class McpCaptureValidatorFixture
{
    private static CapturedServer Remote(string name = "memory", string url = "https://memory.homelab.example/mcp", params CapturedValue[] headers) =>
        new(name, "http", url, null, [], headers, []);

    private static CapturedServer Local(string name = "github", string command = "npx", string[]? args = null, params CapturedValue[] env) =>
        new(name, "stdio", null, command, args ?? ["-y", "@example/github-mcp@1.4.2"], [], env);

    private static CaptureRequest Capture(params CapturedServer[] servers) => new("dev-laptop", "claude-code", "user", servers);

    private static IReadOnlyList<string> Errors(params CapturedServer[] servers) => McpCaptureValidator.Validate(Capture(servers));

    [Fact]
    public void should_accept_a_remote_and_a_local_server_with_variables_and_harmless_literals()
    {
        var errors = Errors(
            Remote(headers: [new CapturedValue("X-Api-Key", "MEMORY_MCP_KEY", null), new CapturedValue("Accept", null, "application/json"), new CapturedValue("X-Team", null, null)]),
            Local(env: [new CapturedValue("GITHUB_TOKEN", "GITHUB_TOKEN", null), new CapturedValue("LOG_LEVEL", null, "info")]));

        errors.Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_literal_under_a_credential_name_without_echoing_it()
    {
        var secret = "not-really-a-secret-but-a-value";

        var errors = Errors(Remote(headers: [new CapturedValue("Authorization", null, "Bearer " + secret)]));

        errors.Should().ContainSingle().Which.Should().Contain("servers[0].headers.Authorization").And.Contain("name of the variable").And.NotContain(secret);
    }

    [Theory]
    [InlineData("ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("password=hunter2")]
    public void should_refuse_a_value_that_looks_like_a_secret_even_under_a_harmless_name(string literal)
    {
        var errors = Errors(Remote(headers: [new CapturedValue("X-Team", null, literal)]));

        errors.Should().ContainSingle().Which.Should().Contain("servers[0].headers.X-Team").And.Contain("secret").And.NotContain(literal);
    }

    [Fact]
    public void should_refuse_a_key_in_the_address_or_an_argument()
    {
        var token = "ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789";

        Errors(Remote(url: "https://user:pass@memory.homelab.example/mcp")).Should().ContainSingle().Which.Should().Contain("without credentials");
        Errors(Remote(url: $"https://memory.homelab.example/mcp?key={token}")).Should().ContainSingle().Which.Should().Contain("servers[0]: url").And.NotContain(token);
        Errors(Local(args: ["--token", token])).Should().ContainSingle().Which.Should().Contain("servers[0].args[1]").And.NotContain(token);
    }

    [Theory]
    [InlineData("npx -y pkg && rm -rf /")]
    [InlineData("sh; reboot")]
    public void should_refuse_a_shell_line_as_a_command(string command)
    {
        Errors(Local(command: command)).Should().ContainSingle().Which.Should().Contain("not a shell line");
    }

    [Fact]
    public void should_refuse_a_variable_that_is_not_a_name_and_both_a_variable_and_a_literal()
    {
        Errors(Local(env: [new CapturedValue("TOKEN", "not-a-name", null)])).Should().ContainSingle().Which.Should().Contain("environment variable");
        Errors(Local(env: [new CapturedValue("LOG_LEVEL", "LOG", "info")])).Should().ContainSingle().Which.Should().Contain("not both");
    }

    [Theory]
    [InlineData("notepad", "user", "client must be one of")]
    [InlineData("claude-code", "everywhere", "scope must be one of")]
    public void should_refuse_an_unknown_client_or_scope(string client, string scope, string expected)
    {
        var errors = McpCaptureValidator.Validate(new CaptureRequest("dev-laptop", client, scope, [Remote()]));

        errors.Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public void should_refuse_a_workstation_that_is_not_an_id_an_empty_capture_and_a_name_used_twice()
    {
        McpCaptureValidator.Validate(new CaptureRequest("My PC", "cursor", "user", [Remote()])).Should().ContainSingle().Which.Should().Contain("workstation must be the id");
        McpCaptureValidator.Validate(Capture()).Should().ContainSingle().Which.Should().Contain("between 1 and 50");
        Errors(Remote("memory"), Remote("Memory", "https://other.homelab.example/mcp")).Should().ContainSingle().Which.Should().Contain("used twice");
    }

    [Fact]
    public void should_refuse_a_remote_server_with_a_command_and_a_local_server_with_an_address()
    {
        Errors(Remote() with { Command = "npx" }).Should().ContainSingle().Which.Should().Contain("not a command");
        Errors(Local() with { Url = "https://a.homelab.example/mcp" }).Should().ContainSingle().Which.Should().Contain("not an url");
        Errors(Remote() with { Transport = "websocket" }).Should().ContainSingle().Which.Should().Contain("transport must be");
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void should_accept_at_most_the_largest_number_of_servers(int count, bool accepted)
    {
        var servers = Enumerable.Range(0, count).Select(i => Remote(name: $"server-{i}", url: $"https://s{i}.homelab.example/mcp")).ToArray();

        Errors(servers).Count.Should().Be(accepted ? 0 : 1);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void should_accept_at_most_the_largest_number_of_arguments(int count, bool accepted)
    {
        var args = Enumerable.Range(0, count).Select(i => $"a{i}").ToArray();

        Errors(Local(args: args)).Count.Should().Be(accepted ? 0 : 1);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void should_accept_at_most_the_largest_number_of_headers(int count, bool accepted)
    {
        var headers = Enumerable.Range(0, count).Select(i => new CapturedValue($"X-Header-{i}", null, "value")).ToArray();

        Errors(Remote(headers: headers)).Count.Should().Be(accepted ? 0 : 1);
    }

    [Theory]
    [InlineData("https://memory.homelab.example/mcp#fragment")]
    [InlineData("https://memory.homelab.example/mcp\u0007")]
    public void should_refuse_a_url_with_a_fragment_or_a_control_character(string url)
    {
        Errors(Remote(url: url)).Should().NotBeEmpty();
    }

    [Fact]
    public void should_refuse_a_name_a_command_and_a_literal_with_a_control_character_or_too_long()
    {
        Errors(Remote(name: "mem\u0001ory")).Should().NotBeEmpty();
        Errors(Local(command: new string('c', 261))).Should().NotBeEmpty();
        Errors(Local(args: [new string('a', 501)])).Should().NotBeEmpty();
        Errors(Remote(headers: [new CapturedValue("Accept", null, "line\nbreak")])).Should().NotBeEmpty();
    }
}
