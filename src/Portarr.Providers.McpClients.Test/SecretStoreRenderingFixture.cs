using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using CatalogModel = Portarr.Core.Catalog.Catalog;

namespace Portarr.Providers.McpClients.Test;

/// <summary>
/// The secrets stay in the operator's vault (D-116): the rendering names the keys a workstation must be able to read and gives the command
/// that reads them into the variables the client uses, so that no value passes through Portarr, an assistant or its provider.
/// </summary>
public sealed class SecretStoreRenderingFixture
{
    private const string Vault = """
        secretStore: { kind: vault, address: "https://vault.homelab.example/", prefix: kv/mcp }
        """;

    private const string Azure = """
        secretStore: { kind: azure-key-vault, address: "https://myvault.vault.azure.net", prefix: mcp- }
        """;

    private const string Servers = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp", headers: { X-Api-Key: { env: MEMORY_MCP_KEY, secret: memory }, X-Team: { env: TEAM, secret: team } } }
          - id: github
            transport: stdio
            stdio: { command: npx, args: ["-y", "pkg@1.0.0"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN, secret: github }, SAME: { env: GITHUB_TOKEN, secret: github }, LOG_LEVEL: info } }
          - id: plain
            transport: http
            http: { url: "https://plain.homelab.example/mcp", headers: { X-Api-Key: { env: PLAIN_KEY } } }
        """;

    private static CatalogModel Parse(string store) => RealMcp.Parse(store + "\n" + Servers).Catalog;

    private static McpRendering Install(CatalogModel catalog, string server, string os)
    {
        var definition = catalog.McpServerList.Single(s => s.Id == server);
        return RealMcp.Rendering(catalog).Install(definition, "claude-code", "user", os);
    }

    [Fact]
    public void should_give_a_posix_shell_the_command_that_reads_the_vault_into_the_variables()
    {
        var rendering = Install(Parse(Vault), "memory", McpOperatingSystems.Linux);

        var provision = rendering.Operations.Should().ContainSingle(o => o.Kind == McpOperationKinds.Provision).Subject;
        provision.Text.Should().Be("""
            export VAULT_ADDR='https://vault.homelab.example'
            export MEMORY_MCP_KEY="$(vault kv get -field=value kv/mcp/memory)"
            export TEAM="$(vault kv get -field=value kv/mcp/team)"
            """);
        provision.Description.Should().Contain("shell profile");
        rendering.RequiresSecrets.Should().Equal("kv/mcp/memory", "kv/mcp/team");
        rendering.RequiresEnv.Should().Equal("MEMORY_MCP_KEY", "TEAM");
        rendering.Notes.Should().Contain(note => note.Contains("vault login", StringComparison.Ordinal));
    }

    [Fact]
    public void should_give_powershell_its_own_form_on_windows()
    {
        var provision = Install(Parse(Vault), "memory", McpOperatingSystems.Windows).Operations.Single(o => o.Kind == McpOperationKinds.Provision);

        provision.Text.Should().Be("""
            $env:VAULT_ADDR = 'https://vault.homelab.example'
            $env:MEMORY_MCP_KEY = (vault kv get -field=value kv/mcp/memory)
            $env:TEAM = (vault kv get -field=value kv/mcp/team)
            """);
        provision.Description.Should().StartWith("In PowerShell");
    }

    [Theory]
    [InlineData(McpOperatingSystems.Linux, "export GITHUB_TOKEN=\"$(az keyvault secret show --vault-name myvault --name mcp-github --query value -o tsv)\"")]
    [InlineData(McpOperatingSystems.Windows, "$env:GITHUB_TOKEN = az keyvault secret show --vault-name myvault --name mcp-github --query value -o tsv")]
    public void should_read_an_azure_key_vault_with_the_azure_cli_and_name_the_secret_with_the_prefix(string os, string expected)
    {
        var rendering = Install(Parse(Azure), "github", os);

        rendering.Operations.Single(o => o.Kind == McpOperationKinds.Provision).Text.Should().Be(expected);
        rendering.RequiresSecrets.Should().Equal("myvault/mcp-github");
        rendering.Notes.Should().Contain(note => note.Contains("Key Vault Secrets User", StringComparison.Ordinal));
    }

    [Fact]
    public void should_read_a_secret_once_per_variable_even_when_two_values_share_it()
    {
        var rendering = Install(Parse(Vault), "github", McpOperatingSystems.Linux);

        rendering.Operations.Single(o => o.Kind == McpOperationKinds.Provision).Text.Split('\n').Should().HaveCount(2);
        rendering.RequiresSecrets.Should().Equal("kv/mcp/github");
    }

    [Fact]
    public void should_add_nothing_for_a_server_without_a_secret_or_a_removal_or_a_catalog_without_a_store()
    {
        var catalog = Parse(Vault);

        Install(catalog, "plain", McpOperatingSystems.Linux).Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
        Install(catalog, "plain", McpOperatingSystems.Linux).RequiresSecrets.Should().BeEmpty();
        RealMcp.Rendering(catalog).Remove(catalog.McpServerList[0], "claude-code", "user", "linux").Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
        RealMcp.Rendering(Parse("")).Install(catalog.McpServerList[0], "claude-code", "user", "linux").Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
    }

    [Fact]
    public void should_never_write_a_secret_value_only_the_variable_the_client_reads()
    {
        var rendering = Install(Parse(Vault), "memory", McpOperatingSystems.Linux);

        rendering.Operations.First(o => o.Kind == McpOperationKinds.Command).Text.Should().Contain("${MEMORY_MCP_KEY}");
        rendering.Operations.Should().OnlyContain(o => o.Kind == McpOperationKinds.Provision || !o.Text.Contains("kv/mcp", StringComparison.Ordinal));
    }

    [Fact]
    public void should_name_the_vault_keys_a_workstation_must_be_able_to_read_in_its_plan()
    {
        var catalog = Parse(Vault + "\nworkstations:\n  - { id: pc, os: linux, clients: [claude-code], mcp: [memory, github, plain] }");
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(catalog, "catalog.yaml", true, [], null));
        var health = Substitute.For<IMcpHealth>();
        health.Current.Returns(new Dictionary<string, McpServerHealth>());
        var servers = new McpServerService(catalogService, health);
        var service = new McpWorkstationService(catalogService, servers, new McpRenderingService(RealMcp.Clients, RealMcp.Stores, catalogService));

        var plan = service.GetPlan(catalog.WorkstationList.Single());

        plan.RequiresSecrets.Should().Equal("kv/mcp/github", "kv/mcp/memory", "kv/mcp/team");
        plan.Install.SelectMany(r => r.Operations).Count(o => o.Kind == McpOperationKinds.Provision).Should().Be(2);
    }
}
