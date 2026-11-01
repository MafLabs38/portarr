using Portarr.Core.Catalog;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Backrest.Test;

/// <summary>The <c>backrest:</c> section of an environment (D-80, D-105): what the connector accepts and refuses.</summary>
public sealed class BackrestEnvironmentConnectorFixture : EnvironmentConnectorContract
{
    protected override IEnvironmentConnector Connector { get; } = new BackrestEnvironmentConnector();

    protected override string ValidSection => "{ url: \"http://10.0.0.10:9898\", allowInsecure: true, userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD }";

    protected override string? SectionWithCredentialsInUrl => "{ url: \"http://user:pass@10.0.0.10:9898\" }";

    protected override string? SectionWithSecretOutsideNamespace => "{ url: \"http://10.0.0.10:9898\", allowInsecure: true, userEnv: SOME_OTHER_USER, passwordEnv: SOME_OTHER_SECRET }";

    // Backrest's credential variables are not confined to a Portarr namespace yet (issue #45).
    protected override bool SecretVariablesAreUnconfined => true;

    protected override string? SectionWithLiteralSecret => "{ url: \"http://10.0.0.10:9898\", allowInsecure: true, userEnv: \"admin\", passwordEnv: \"pa ss\" }";

    // Stubs stand for the runtimes: an account is not a machine, whatever connector provides it.
    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new BackrestEnvironmentConnector(), new StubEnvironmentConnector("cloudflare", isRuntime: true, singleMachine: false), new StubEnvironmentConnector("docker", isRuntime: true), new StandInKubernetesConnector()]);

    [Fact]
    public void should_read_the_url_and_the_variables_that_hold_the_credentials()
    {
        var parsed = Parse("""
            environments:
              - { id: server-1, docker: { url: "http://10.0.0.10:2375" }, backrest: { url: "http://10.0.0.10:9898", allowInsecure: true, userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }
            """);

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().Be(new BackrestEndpoint(new Uri("http://10.0.0.10:9898/"), "BACKREST_USER", "BACKREST_PASSWORD", AllowInsecure: true));
    }

    [Fact]
    public void should_accept_a_backrest_without_authentication()
    {
        var parsed = Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().Be(new BackrestEndpoint(new Uri("http://10.0.0.10:9898"), null, null));
    }

    [Fact]
    public void should_leave_an_environment_that_declares_no_backrest_without_one()
    {
        var parsed = Parse("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().BeNull();
        parsed.Catalog.Environments[0].Declares("backrest").Should().BeFalse();
    }

    [Fact]
    public void should_ask_for_both_credentials_variables_or_none()
    {
        var result = Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\", allowInsecure: true, userEnv: BACKREST_USER } }\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("must both name an environment variable");
    }

    [Fact]
    public void should_refuse_credentials_over_plain_http_unless_the_operator_accepts_it()
    {
        var refused = Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }\n");
        var secure = Parse("environments:\n  - { id: server-1, backrest: { url: \"https://backrest.homelab.example\", userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }\n");
        var plainWithoutLogin = Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\" } }\n");

        refused.Errors.Should().ContainSingle().Which.Should().Contain("must be https when userEnv and passwordEnv are set").And.Contain("allowInsecure");
        secure.Errors.Should().BeEmpty();
        plainWithoutLogin.Errors.Should().BeEmpty("without a login there is nothing to protect");
    }

    [Theory]
    [InlineData("http://10.0.0.10:9898", "http://10.0.0.10:9898/")]
    [InlineData("https://home.example/backrest", "https://home.example/backrest/")]
    [InlineData("https://home.example/backrest/", "https://home.example/backrest/")]
    public void should_end_the_address_with_a_slash_so_that_a_path_prefix_is_kept(string url, string expected)
    {
        var parsed = Parse($"environments:\n  - {{ id: server-1, backrest: {{ url: \"{url}\" }} }}\n");

        parsed.Catalog.Environments[0].Backrest()!.Url.Should().Be(new Uri(expected));
        new Uri(parsed.Catalog.Environments[0].Backrest()!.Url, "v1.Backrest/GetConfig").AbsoluteUri.Should().StartWith(expected, "the requests stay under the prefix");
    }

    [Theory]
    [InlineData("ftp://10.0.0.10:9898")]
    [InlineData("10.0.0.10:9898")]
    [InlineData("")]
    public void should_refuse_an_address_that_is_not_http(string url)
    {
        var result = Parse($"environments:\n  - {{ id: server-1, backrest: {{ url: \"{url}\" }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("backrest.url must be Backrest's address");
    }

    [Fact]
    public void should_accept_https_and_treat_a_blank_variable_as_omitted()
    {
        var parsed = Parse("environments:\n  - { id: server-1, backrest: { url: \"https://backrest.homelab.example\", userEnv: \"\", passwordEnv: \"\" } }\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Backrest().Should().Be(new BackrestEndpoint(new Uri("https://backrest.homelab.example/"), null, null));
    }

    [Theory]
    [InlineData("kubernetes: { server: \"https://10.0.0.20:6443\" }")]
    [InlineData("cloudflare: { accountId: abc123 }")]
    public void should_reject_a_backrest_on_a_cluster_or_an_account(string connector)
    {
        var result = Parse($"environments:\n  - {{ id: cluster, {connector}, backrest: {{ url: \"http://10.0.0.10:9898\" }} }}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain("not on a kubernetes or cloudflare environment");
    }

    [Fact]
    public void should_refuse_the_section_when_no_connector_owns_it()
    {
        var result = CatalogParser.Parse("environments:\n  - { id: server-1, backrest: { url: \"http://10.0.0.10:9898\" } }\n", []);

        result.Errors.Should().ContainSingle().Which.Should().Contain("backrest").And.Contain("line 2");
    }
}
