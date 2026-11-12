using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogSecretStoreParserFixture
{
    private const string Vault = """
        secretStore:
          kind: vault
          address: https://vault.homelab.example
          prefix: kv/mcp
        """;

    private const string Server = """
        mcpServers:
          - id: memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers: { X-Api-Key: { env: MEMORY_MCP_KEY, secret: memory } }
        """;

    private static IReadOnlyList<string> Errors(string yaml) => TestCatalog.Parse(yaml).Errors;

    [Fact]
    public void should_read_the_store_and_the_key_of_a_secret_behind_a_variable()
    {
        var result = TestCatalog.Parse(Vault + "\n" + Server);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.SecretStore.Should().Be(new McpSecretStore("vault", new Uri("https://vault.homelab.example"), "kv/mcp"));
        var header = result.Catalog.McpServerList.Single().Http!.Headers["X-Api-Key"];
        header.EnvName.Should().Be("MEMORY_MCP_KEY");
        header.SecretKey.Should().Be("memory");
    }

    [Fact]
    public void should_read_an_azure_key_vault_whose_prefix_is_put_before_the_key()
    {
        var result = TestCatalog.Parse("""
            secretStore: { kind: azure-key-vault, address: "https://myvault.vault.azure.net", prefix: mcp- }
            mcpServers:
              - { id: github, transport: stdio, stdio: { command: npx, env: { GITHUB_TOKEN: { env: GITHUB_TOKEN, secret: github-token } } } }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Catalog.SecretStore!.Kind.Should().Be("azure-key-vault");
        result.Catalog.McpServerList.Single().Stdio!.Env["GITHUB_TOKEN"].SecretKey.Should().Be("github-token");
    }

    [Fact]
    public void should_have_no_store_without_the_section()
    {
        TestCatalog.Parse(Server.Replace(", secret: memory", string.Empty, StringComparison.Ordinal)).Catalog.SecretStore.Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_secret_key_when_the_catalog_has_no_store()
    {
        Errors(Server).Should().ContainSingle().Which.Should().Contain("http.headers.X-Api-Key.secret").And.Contain("declares no secretStore");
    }

    [Fact]
    public void should_not_pile_a_second_error_on_a_secret_when_the_store_itself_is_refused()
    {
        var errors = Errors("secretStore: { kind: keychain, address: \"https://v.homelab.example\", prefix: kv/mcp }\n" + Server);

        errors.Should().ContainSingle().Which.Should().Contain("kind must be vault or azure-key-vault");
    }

    [Fact]
    public void should_refuse_a_secret_without_the_variable_that_receives_it()
    {
        Errors(Vault + "\n" + Server.Replace("{ env: MEMORY_MCP_KEY, secret: memory }", "{ secret: memory }", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("secret needs env");
    }

    [Theory]
    [InlineData("memory/key")]
    [InlineData("a b")]
    [InlineData("-x")]
    public void should_refuse_a_key_that_is_not_a_name(string key)
    {
        Errors(Vault + "\n" + Server.Replace("secret: memory", $"secret: \"{key}\"", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("must be the key of the secret").And.Contain("never its value");
    }

    [Fact]
    public void should_refuse_a_secret_name_azure_cannot_hold()
    {
        var yaml = "secretStore: { kind: azure-key-vault, address: \"https://myvault.vault.azure.net\" }\n" + Server.Replace("secret: memory", "secret: memory.key", StringComparison.Ordinal);

        Errors(yaml).Should().ContainSingle().Which.Should().Contain("letters, digits and dashes");
    }

    [Theory]
    [InlineData("kind: vault, address: \"https://user:pw@vault.homelab.example\", prefix: kv/mcp", "address must be")]
    [InlineData("kind: vault, address: \"https://vault.homelab.example/ui\", prefix: kv/mcp", "address must be")]
    [InlineData("kind: vault, address: \"https://vault.homelab.example?x=1\", prefix: kv/mcp", "address must be")]
    [InlineData("kind: vault, address: vault.homelab.example, prefix: kv/mcp", "address must be")]
    [InlineData("kind: vault, address: \"http://10.0.0.5:8200\", prefix: kv/mcp", "allowInsecure")]
    [InlineData("kind: vault, address: \"https://vault.homelab.example\"", "prefix must be the path")]
    [InlineData("kind: vault, address: \"https://vault.homelab.example\", prefix: /kv/mcp/", "prefix must be the path")]
    [InlineData("kind: azure-key-vault, address: \"https://myvault.vault.azure.net\", prefix: \"mcp/\"", "prefix is put before the key")]
    public void should_refuse_a_store_that_is_not_an_address_and_a_place(string store, string expected)
    {
        Errors($"secretStore: {{ {store} }}").Should().Contain(error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void should_accept_a_plain_http_store_only_when_it_is_allowed()
    {
        var result = TestCatalog.Parse("secretStore: { kind: vault, address: \"http://10.0.0.5:8200\", allowInsecure: true, prefix: kv/mcp }");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void should_keep_the_store_out_of_the_sections_an_api_key_or_the_chat_can_edit()
    {
        Portarr.Core.Catalog.Editing.CatalogSections.All.Should().NotContain("secretStore");
        Portarr.Core.Catalog.Editing.CatalogSections.PersonOnly.Should().NotContain("secretStore");
    }
}
