using Portarr.Core.Catalog;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Cloudflare.Test;

/// <summary>The <c>cloudflare:</c> section of an environment (D-53, D-105): what the connector accepts and refuses.</summary>
public sealed class CloudflareEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new CloudflareEnvironmentConnector();

    protected override string ValidSection => "{ accountId: \"0123456789abcdef0123456789abcdef\", tokenEnv: CF_TOKEN }";

    // The token goes to Cloudflare's own API, never to an address the catalog names.
    protected override string? SectionWithCredentialsInUrl => null;

    protected override string? SectionWithSecretOutsideNamespace => null;

    protected override string? SectionWithLiteralSecret => "{ accountId: abc, tokenEnv: \"cf-token-value!\" }";

    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new CloudflareEnvironmentConnector(), new StubEnvironmentConnector("docker", isRuntime: true)]);

    [Fact]
    public void should_read_the_account_and_default_the_token_variable()
    {
        var parsed = Parse("environments:\n  - { id: cdn, cloudflare: { accountId: \"  TODO \" } }\n");

        parsed.Errors.Should().BeEmpty();
        var environment = parsed.Catalog.Environments.Single();
        environment.Cloudflare().Should().Be(new CloudflareAccount("TODO", "CLOUDFLARE_API_TOKEN"));
        environment.Runtime.Should().Be("cloudflare");
    }

    [Theory]
    [InlineData("runtime: cloudflare")]
    [InlineData("runtime: cloudflare\n    cloudflare:")]
    [InlineData("cloudflare: { tokenEnv: CF_TOKEN }")]
    public void should_ask_for_the_account_id(string body)
    {
        var result = Parse($"environments:\n  - id: cdn\n    {body}\n");

        result.Errors.Should().ContainSingle().Which.Should().Be("Environment 'cdn': cloudflare.accountId is required (the id in the Cloudflare dashboard URL).");
    }

    [Fact]
    public void should_refuse_a_cloudflare_next_to_another_runtime()
    {
        var result = Parse("environments:\n  - { id: cdn, cloudflare: { accountId: abc }, docker: { url: \"http://10.0.0.10:2375\" } }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("declare one connector (cloudflare, docker or kubernetes)");
    }

    [Fact]
    public void should_refuse_a_runtime_that_is_not_cloudflare()
    {
        var result = Parse("environments:\n  - { id: cdn, runtime: docker, cloudflare: { accountId: abc } }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("a docker environment cannot declare cloudflare");
    }

    [Fact]
    public void should_not_be_a_machine_that_can_carry_a_tool_or_be_a_host()
    {
        var host = Parse("environments:\n  - { id: cdn, cloudflare: { accountId: abc } }\n  - { id: vm-1, host: cdn }\n");

        host.Errors.Should().ContainSingle().Which.Should().Contain("must be a single machine");
    }
}
